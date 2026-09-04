package main

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"net/url"
	"os"
	"os/signal"
	"sync"
	"syscall"
	"time"

	"controlplane-agent/internal/config"
	"controlplane-agent/internal/lifecycle"
	"controlplane-agent/internal/metrics"
	"controlplane-agent/internal/packages"
	"controlplane-agent/internal/runner"

	"github.com/gorilla/websocket"
)

var (
	Version = "1.1.0"
)

type HeartbeatPayload struct {
	Type           string                   `json:"type"`
	NodeID         string                   `json:"nodeId"`
	Hostname       string                   `json:"hostname"`
	AgentVersion   string                   `json:"agentVersion"`
	KernelVersion  string                   `json:"kernelVersion"`
	PendingReboot  bool                     `json:"pendingReboot"`
	PackageManager string                   `json:"packageManager"`
	Metrics        *metrics.Metrics         `json:"metrics"`
	PackageSummary *packages.PackageSummary `json:"packageSummary"`
}

type CommandEnvelope struct {
	Type    string   `json:"type"`
	JobID   string   `json:"jobId"`
	Command string   `json:"command"`
	Args    []string `json:"args"`
}

type UpdateEnvelope struct {
	Type          string `json:"type"`
	JobID         string `json:"jobId"`
	DownloadURL   string `json:"downloadUrl"`
	TargetVersion string `json:"targetVersion"`
}

type FrameEnvelope struct {
	Type   string       `json:"type"`
	NodeID string       `json:"nodeId"`
	Frame  runner.Frame `json:"frame"`
}

func main() {
	cfg, err := config.LoadConfig(Version)
	if err != nil {
		log.Fatalf("Configuration error: %v", err)
	}

	collector := metrics.NewCollector()
	pkgInspector := packages.DetectInspector()
	procRunner := runner.NewProcessRunner()

	// Test mode: output vitals to stdout and exit
	if cfg.TestMetrics {
		runTestMetrics(collector, pkgInspector)
		return
	}

	hostname, _ := os.Hostname()
	log.Printf("[Agent %s] Starting ControlPlane agent for node '%s' (ID: %s)", Version, hostname, cfg.NodeID)

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	sigChan := make(chan os.Signal, 1)
	signal.Notify(sigChan, os.Interrupt, syscall.SIGTERM)

	go func() {
		sig := <-sigChan
		log.Printf("[Agent] Received shutdown signal %s, initiating graceful shutdown...", sig)
		cancel()
	}()

	// Outbound persistent connection loop
	backoff := 1 * time.Second
	maxBackoff := 30 * time.Second

	for {
		select {
		case <-ctx.Done():
			log.Println("[Agent] Exiting cleanly.")
			return
		default:
		}

		err := runAgentSession(ctx, cfg, hostname, collector, pkgInspector, procRunner)
		if err != nil && ctx.Err() == nil {
			log.Printf("[Agent] Session ended with error: %v. Reconnecting in %v...", err, backoff)
			select {
			case <-time.After(backoff):
			case <-ctx.Done():
				return
			}
			backoff *= 2
			if backoff > maxBackoff {
				backoff = maxBackoff
			}
		} else {
			backoff = 1 * time.Second
		}
	}
}

func runAgentSession(
	ctx context.Context,
	cfg *config.Config,
	hostname string,
	collector metrics.Collector,
	pkgInspector packages.Inspector,
	procRunner *runner.ProcessRunner,
) error {
	u, err := url.Parse(cfg.HubURL)
	if err != nil {
		return fmt.Errorf("invalid hub url: %w", err)
	}

	headers := make(http.Header)
	if cfg.Token != "" {
		headers.Set("Authorization", "Bearer "+cfg.Token)
		headers.Set("X-ControlPlane-Node-Id", cfg.NodeID)
	}

	log.Printf("[Agent] Dialing hub at %s...", u.String())
	dialer := websocket.DefaultDialer
	conn, resp, err := dialer.DialContext(ctx, u.String(), headers)
	if err != nil {
		if resp != nil {
			return fmt.Errorf("handshake failed with status %d: %w", resp.StatusCode, err)
		}
		return fmt.Errorf("dial failed: %w", err)
	}
	defer conn.Close()

	log.Printf("[Agent] Connected to hub. Node registration verified.")

	var writeMu sync.Mutex
	writeJSON := func(v interface{}) error {
		writeMu.Lock()
		defer writeMu.Unlock()
		conn.SetWriteDeadline(time.Now().Add(5 * time.Second))
		return conn.WriteJSON(v)
	}

	// Send initial heartbeat immediately upon connection
	sendHeartbeat(cfg, hostname, collector, pkgInspector, writeJSON)

	// Periodic heartbeat timer
	ticker := time.NewTicker(cfg.HeartbeatInterval)
	defer ticker.Stop()

	errChan := make(chan error, 2)

	// Heartbeat sender loop
	go func() {
		for {
			select {
			case <-ctx.Done():
				return
			case <-ticker.C:
				if err := sendHeartbeat(cfg, hostname, collector, pkgInspector, writeJSON); err != nil {
					log.Printf("[Agent] Heartbeat transmission failed: %v", err)
					errChan <- err
					return
				}
			}
		}
	}()

	// Incoming message reader loop
	go func() {
		for {
			_, message, err := conn.ReadMessage()
			if err != nil {
				errChan <- err
				return
			}

			var base struct {
				Type string `json:"type"`
			}
			if err := json.Unmarshal(message, &base); err != nil {
				continue
			}

			if base.Type == "CMD_REBOOT" {
				var cmd CommandEnvelope
				if err := json.Unmarshal(message, &cmd); err == nil {
					go func(envelope CommandEnvelope) {
						log.Printf("[Agent] Handling CMD_REBOOT for Job %s", envelope.JobID)
						_ = lifecycle.TriggerReboot(ctx, envelope.JobID, cfg.NodeID, writeJSON)
					}(cmd)
				}
			} else if base.Type == "CMD_SELF_UPDATE" {
				var updateEnv UpdateEnvelope
				if err := json.Unmarshal(message, &updateEnv); err == nil {
					go func(envelope UpdateEnvelope) {
						log.Printf("[Agent] Handling CMD_SELF_UPDATE for Job %s (Target: %s)", envelope.JobID, envelope.TargetVersion)
						_ = lifecycle.PerformSelfUpdate(ctx, envelope.JobID, cfg.NodeID, envelope.DownloadURL, envelope.TargetVersion, cfg.Token, writeJSON)
					}(updateEnv)
				}
			} else if base.Type == "EXECUTE_COMMAND" {
				var cmd CommandEnvelope
				if err := json.Unmarshal(message, &cmd); err == nil {
					go func(envelope CommandEnvelope) {
						log.Printf("[Agent] Executing command for Job %s: %s %v", envelope.JobID, envelope.Command, envelope.Args)
						_ = procRunner.ExecuteCommand(ctx, envelope.JobID, envelope.Command, envelope.Args, func(f runner.Frame) {
							_ = writeJSON(FrameEnvelope{
								Type:   "FRAME",
								NodeID: cfg.NodeID,
								Frame:  f,
							})
						})
					}(cmd)
				}
			}
		}
	}()

	select {
	case <-ctx.Done():
		// Send normal closure frame
		writeMu.Lock()
		_ = conn.WriteMessage(websocket.CloseMessage, websocket.FormatCloseMessage(websocket.CloseNormalClosure, "agent stopping"))
		writeMu.Unlock()
		return nil
	case err := <-errChan:
		return err
	}
}

func sendHeartbeat(
	cfg *config.Config,
	hostname string,
	collector metrics.Collector,
	pkgInspector packages.Inspector,
	writeFn func(interface{}) error,
) error {
	m, _ := collector.Collect()
	pkg, _ := pkgInspector.Inspect(context.Background())

	payload := HeartbeatPayload{
		Type:           "HEARTBEAT",
		NodeID:         cfg.NodeID,
		Hostname:       hostname,
		AgentVersion:   cfg.Version,
		KernelVersion:  collector.KernelVersion(),
		PendingReboot:  collector.IsRebootRequired(),
		PackageManager: pkg.PackageManager,
		Metrics:        m,
		PackageSummary: pkg,
	}

	return writeFn(payload)
}

func runTestMetrics(collector metrics.Collector, pkgInspector packages.Inspector) {
	m, err := collector.Collect()
	if err != nil {
		log.Printf("Metrics collection error: %v", err)
	}

	pkg, err := pkgInspector.Inspect(context.Background())
	if err != nil {
		log.Printf("Package inspection error: %v", err)
	}

	output := map[string]interface{}{
		"kernelVersion":  collector.KernelVersion(),
		"pendingReboot":  collector.IsRebootRequired(),
		"metrics":        m,
		"packageSummary": pkg,
	}

	jsonBytes, _ := json.MarshalIndent(output, "", "  ")
	fmt.Println(string(jsonBytes))
}
