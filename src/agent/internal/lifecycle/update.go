package lifecycle

import (
	"context"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"time"
)

// UpdateCommencingMessage is sent when the agent starts downloading the update binary.
type UpdateCommencingMessage struct {
	Type          string `json:"type"`
	NodeID        string `json:"nodeId"`
	JobID         string `json:"jobId"`
	TargetVersion string `json:"targetVersion"`
	Timestamp     string `json:"timestamp"`
}

// UpdateAppliedMessage is sent when the binary is verified and replaced on disk before restart.
type UpdateAppliedMessage struct {
	Type          string `json:"type"`
	NodeID        string `json:"nodeId"`
	JobID         string `json:"jobId"`
	TargetVersion string `json:"targetVersion"`
	Success       bool   `json:"success"`
	Error         string `json:"error,omitempty"`
	Timestamp     string `json:"timestamp"`
}

// PerformSelfUpdate downloads the new binary, validates it, atomically replaces the running executable,
// notifies the server, and restarts the agent service.
func PerformSelfUpdate(
	ctx context.Context,
	jobID string,
	nodeID string,
	downloadURL string,
	targetVersion string,
	token string,
	writeJSON func(interface{}) error,
) error {
	log.Printf("[Agent] Self-update initiated for Job %s (Target: %s, URL: %s)...", jobID, targetVersion, downloadURL)

	_ = writeJSON(UpdateCommencingMessage{
		Type:          "UPDATE_COMMENCING",
		NodeID:        nodeID,
		JobID:         jobID,
		TargetVersion: targetVersion,
		Timestamp:     time.Now().UTC().Format(time.RFC3339),
	})

	tempBinaryPath := fmt.Sprintf("/tmp/controlplane-agent-%d.tmp", time.Now().UnixNano())
	defer os.Remove(tempBinaryPath)

	// 1. Download updated binary
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, downloadURL, nil)
	if err != nil {
		return failUpdate(writeJSON, nodeID, jobID, targetVersion, fmt.Errorf("failed to create download request: %w", err))
	}

	if token != "" {
		req.Header.Set("X-ControlPlane-Key", token)
		req.Header.Set("Authorization", "Bearer "+token)
	}

	client := &http.Client{Timeout: 5 * time.Minute}
	resp, err := client.Do(req)
	if err != nil {
		return failUpdate(writeJSON, nodeID, jobID, targetVersion, fmt.Errorf("download request failed: %w", err))
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return failUpdate(writeJSON, nodeID, jobID, targetVersion, fmt.Errorf("download server returned HTTP %d", resp.StatusCode))
	}

	out, err := os.OpenFile(tempBinaryPath, os.O_CREATE|os.O_WRONLY|os.O_TRUNC, 0755)
	if err != nil {
		return failUpdate(writeJSON, nodeID, jobID, targetVersion, fmt.Errorf("failed to create temp file: %w", err))
	}

	if _, err := io.Copy(out, resp.Body); err != nil {
		out.Close()
		return failUpdate(writeJSON, nodeID, jobID, targetVersion, fmt.Errorf("failed writing downloaded binary: %w", err))
	}
	out.Close()

	// Ensure executable permissions
	if err := os.Chmod(tempBinaryPath, 0755); err != nil {
		return failUpdate(writeJSON, nodeID, jobID, targetVersion, fmt.Errorf("failed chmod on temp binary: %w", err))
	}

	// 2. Pre-flight sanity check on the downloaded binary
	testCmd := exec.CommandContext(ctx, tempBinaryPath, "-test-metrics")
	if output, err := testCmd.CombinedOutput(); err != nil {
		return failUpdate(writeJSON, nodeID, jobID, targetVersion, fmt.Errorf("pre-flight sanity check failed on new binary: %w (output: %s)", err, string(output)))
	}
	log.Printf("[Agent] Pre-flight verification succeeded on updated binary.")

	// 3. Locate target executable path
	currentExe, err := os.Executable()
	if err != nil {
		return failUpdate(writeJSON, nodeID, jobID, targetVersion, fmt.Errorf("could not determine current executable path: %w", err))
	}

	if resolved, err := filepath.EvalSymlinks(currentExe); err == nil {
		currentExe = resolved
	}

	// 4. Atomically replace executable on disk
	log.Printf("[Agent] Replacing %s with updated binary...", currentExe)
	if err := replaceExecutable(tempBinaryPath, currentExe); err != nil {
		return failUpdate(writeJSON, nodeID, jobID, targetVersion, fmt.Errorf("atomic binary replacement failed: %w", err))
	}

	log.Printf("[Agent] Successfully installed agent %s to %s.", targetVersion, currentExe)

	// 5. Notify server of successful update
	_ = writeJSON(UpdateAppliedMessage{
		Type:          "UPDATE_APPLIED",
		NodeID:        nodeID,
		JobID:         jobID,
		TargetVersion: targetVersion,
		Success:       true,
		Timestamp:     time.Now().UTC().Format(time.RFC3339),
	})

	// Wait 300ms for websocket frame flush
	time.Sleep(300 * time.Millisecond)

	// 6. Trigger service restart
	go func() {
		time.Sleep(200 * time.Millisecond)
		restartCmd := exec.Command("systemctl", "restart", "controlplane-agent")
		if err := restartCmd.Start(); err != nil {
			log.Printf("[Agent] systemctl restart failed (%v), terminating process for supervisor restart...", err)
			os.Exit(0)
		}
	}()

	return nil
}

func replaceExecutable(src, dst string) error {
	// First attempt atomic rename
	err := os.Rename(src, dst)
	if err == nil {
		return nil
	}

	// Fallback to mv -f if across filesystems or ETXTBSY on rename
	cmd := exec.Command("mv", "-f", src, dst)
	if output, err := cmd.CombinedOutput(); err != nil {
		return fmt.Errorf("mv -f failed: %w (output: %s)", err, string(output))
	}

	return nil
}

func failUpdate(writeJSON func(interface{}) error, nodeID, jobID, targetVersion string, err error) error {
	log.Printf("[Agent] Self-update failed: %v", err)
	_ = writeJSON(UpdateAppliedMessage{
		Type:          "UPDATE_APPLIED",
		NodeID:        nodeID,
		JobID:         jobID,
		TargetVersion: targetVersion,
		Success:       false,
		Error:         err.Error(),
		Timestamp:     time.Now().UTC().Format(time.RFC3339),
	})
	return err
}
