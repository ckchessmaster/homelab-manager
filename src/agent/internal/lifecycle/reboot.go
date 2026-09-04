package lifecycle

import (
	"context"
	"fmt"
	"log"
	"os/exec"
	"runtime"
	"time"
)

// RebootCommencingMessage is sent to the backend before the agent triggers system reboot.
type RebootCommencingMessage struct {
	Type      string `json:"type"`
	NodeID    string `json:"nodeId"`
	JobID     string `json:"jobId"`
	Timestamp string `json:"timestamp"`
}

// TriggerReboot logs the reboot request, dispatches REBOOT_COMMENCING over WebSocket,
// allows buffers to flush, and triggers the operating system reboot.
func TriggerReboot(
	ctx context.Context,
	jobID string,
	nodeID string,
	writeJSON func(interface{}) error,
) error {
	log.Printf("[Agent] Reboot signal received for Job %s. Flushing buffers...", jobID)

	msg := RebootCommencingMessage{
		Type:      "REBOOT_COMMENCING",
		NodeID:    nodeID,
		JobID:     jobID,
		Timestamp: time.Now().UTC().Format(time.RFC3339),
	}

	if err := writeJSON(msg); err != nil {
		log.Printf("[Agent] Failed to send REBOOT_COMMENCING message: %v", err)
	} else {
		log.Printf("[Agent] REBOOT_COMMENCING message sent successfully.")
	}

	// Small pause to ensure TCP/WebSocket frame transmission completes before process termination
	select {
	case <-time.After(500 * time.Millisecond):
	case <-ctx.Done():
		return ctx.Err()
	}

	var cmd *exec.Cmd
	if runtime.GOOS == "windows" {
		cmd = exec.Command("shutdown", "/r", "/t", "0")
	} else {
		cmd = exec.Command("systemctl", "reboot")
	}

	log.Printf("[Agent] Invoking OS reboot command: %s %v", cmd.Path, cmd.Args)
	if err := cmd.Run(); err != nil {
		log.Printf("[Agent] OS reboot execution error: %v", err)
		return fmt.Errorf("os reboot failed: %w", err)
	}

	return nil
}
