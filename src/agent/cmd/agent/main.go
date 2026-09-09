package main

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"math/rand"
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
	Version = "1.1.1"
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
	Type          string   `json:"type"`
	JobID         string   `json:"jobId"`
	DownloadURL   string   `json:"downloadUrl"`
	TargetVersion string   `json:"targetVersion"`
	Command       string   `json:"command"`
	Args          []string `json:"args"`
}

type FrameEnvelope struct {
	Type   string       `json:"type"`
	NodeID string       `json:"nodeId"`
	Frame  runner.Frame `json:"frame"`
}

type PackageCache struct {
	mu      sync.RWMutex
	summary *packages.PackageSummary
}

func (pc *PackageCache) Get() *packages.PackageSummary {
	pc.mu.RLock()
	defer pc.mu.RUnlock()
	return pc.summary
}

func (pc *PackageCache) Set(s *packages.PackageSummary) {
	pc.mu.Lock()
	defer pc.mu.Unlock()
	pc.summary = s
}

const (
	writeWait  = 5 * time.Second
	pongWait   = 35 * time.Second
	pingPeriod = 15 * time.Second
)

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

	pkgCache := &PackageCache{}

	// Initial package inspection in background immediately
	go func() {
		inspectCtx, inspectCancel := context.WithTimeout(context.Background(), 30*time.Second)
		defer inspectCancel()
		if s, err := pkgInspector.Inspect(inspectCtx); err == nil {
			pkgCache.Set(s)
		}
	}()

	// Periodic package inspection every 10 minutes
	go func() {
		pkgTicker := time.NewTicker(10 * time.Minute)
		defer pkgTicker.Stop()
		for {
			select {
			case <-ctx.Done():
				return
			case <-pkgTicker.C:
				inspectCtx, inspectCancel := context.WithTimeout(context.Background(), 60*time.Second)
				if s, err := pkgInspector.Inspect(inspectCtx); err == nil {
					pkgCache.Set(s)
				}
				inspectCancel()
			}
		}
	}()

	// Outbound persistent connection loop
	backoff := 1 * time.Second
	maxBackoff := 15 * time.Second

	for {
		select {
		case <-ctx.Done():
			log.Println("[Agent] Exiting cleanly.")
			return
		default:
		}

		sessionStart := time.Now()
		err := runAgentSession(ctx, cfg, hostname, collector, pkgCache, procRunner, func() {
			// Successfully connected & registered
			backoff = 1 * time.Second
		})

		if ctx.Err() != nil {
			log.Println("[Agent] Exiting cleanly.")
			return
		}

		// If session was connected for more than 10 seconds, reset backoff for fast recovery
		if time.Since(sessionStart) > 10*time.Second {
			backoff = 1 * time.Second
		}

		if err != nil {
			// Add slight jitter (0-500ms) to prevent thundering herd
			jitter := time.Duration(rand.Intn(500)) * time.Millisecond
			sleepDuration := backoff + jitter
			log.Printf("[Agent] Session ended with error: %v. Reconnecting in %v...", err, sleepDuration.Round(time.Millisecond))

			select {
			case <-time.After(sleepDuration):
			case <-ctx.Done():
				return
			}

			backoff *= 2
			if backoff > maxBackoff {
				backoff = maxBackoff
			}
		}
	}
}

func runAgentSession(
	ctx context.Context,
	cfg *config.Config,
	hostname string,
	collector metrics.Collector,
	pkgCache *PackageCache,
	procRunner *runner.ProcessRunner,
	onConnected func(),
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
	headers.Set("X-ControlPlane-Hostname", hostname)

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
	if onConnected != nil {
		onConnected()
	}

	var writeMu sync.Mutex
	writeJSON := func(v interface{}) error {
		writeMu.Lock()
		defer writeMu.Unlock()
		conn.SetWriteDeadline(time.Now().Add(writeWait))
		return conn.WriteJSON(v)
	}

	// Set initial read deadline
	conn.SetReadDeadline(time.Now().Add(pongWait))

	// Keepalive: handle incoming ping frames from server by responding with pong and extending deadline
	conn.SetPingHandler(func(appData string) error {
		conn.SetReadDeadline(time.Now().Add(pongWait))
		writeMu.Lock()
		defer writeMu.Unlock()
		return conn.WriteControl(websocket.PongMessage, []byte(appData), time.Now().Add(writeWait))
	})

	// Keepalive: handle incoming pong frames from server by extending deadline
	conn.SetPongHandler(func(string) error {
		conn.SetReadDeadline(time.Now().Add(pongWait))
		return nil
	})

	// Send initial heartbeat immediately upon connection
	sendHeartbeat(cfg, hostname, collector, pkgCache, writeJSON)

	// Periodic heartbeat timer
	ticker := time.NewTicker(cfg.HeartbeatInterval)
	defer ticker.Stop()

	// Periodic client ping ticker
	pingTicker := time.NewTicker(pingPeriod)
	defer pingTicker.Stop()

	errChan := make(chan error, 3)

	// Heartbeat sender loop
	go func() {
		for {
			select {
			case <-ctx.Done():
				return
			case <-ticker.C:
				if err := sendHeartbeat(cfg, hostname, collector, pkgCache, writeJSON); err != nil {
					log.Printf("[Agent] Heartbeat transmission failed: %v", err)
					errChan <- err
					return
				}
			}
		}
	}()

	// Outgoing ping sender loop (keeps NAT tables active and triggers pong)
	go func() {
		for {
			select {
			case <-ctx.Done():
				return
			case <-pingTicker.C:
				writeMu.Lock()
				err := conn.WriteControl(websocket.PingMessage, []byte{}, time.Now().Add(writeWait))
				writeMu.Unlock()
				if err != nil {
					log.Printf("[Agent] Ping transmission failed: %v", err)
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
			conn.SetReadDeadline(time.Now().Add(pongWait))

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
					if updateEnv.DownloadURL == "" && updateEnv.Command != "" {
						updateEnv.DownloadURL = updateEnv.Command
					}
					if updateEnv.TargetVersion == "" && len(updateEnv.Args) > 0 {
						updateEnv.TargetVersion = updateEnv.Args[0]
					}
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
	pkgCache *PackageCache,
	writeFn func(interface{}) error,
) error {
	m, _ := collector.Collect()
	pkg := pkgCache.Get()

	pkgManager := ""
	if pkg != nil {
		pkgManager = pkg.PackageManager
	}

	payload := HeartbeatPayload{
		Type:           "HEARTBEAT",
		NodeID:         cfg.NodeID,
		Hostname:       hostname,
		AgentVersion:   cfg.Version,
		KernelVersion:  collector.KernelVersion(),
		PendingReboot:  collector.IsRebootRequired(),
		PackageManager: pkgManager,
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
