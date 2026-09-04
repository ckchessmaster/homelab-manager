package config

import (
	"flag"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

type Config struct {
	HubURL            string
	Token             string
	NodeID            string
	HeartbeatInterval time.Duration
	TestMetrics       bool
	Version           string
}

func LoadConfig(version string) (*Config, error) {
	cfg := &Config{
		Version: version,
	}

	var intervalSec int

	flag.StringVar(&cfg.HubURL, "hub-url", getEnv("CONTROLPLANE_HUB_URL", "ws://localhost:5000/agent-hub"), "WebSocket URL of the ControlPlane hub")
	flag.StringVar(&cfg.Token, "token", getEnv("CONTROLPLANE_TOKEN", ""), "Authentication node token")
	flag.StringVar(&cfg.NodeID, "node-id", getEnv("CONTROLPLANE_NODE_ID", ""), "Persistent Node ID (UUID)")
	flag.IntVar(&intervalSec, "heartbeat-interval", 10, "Heartbeat interval in seconds")
	flag.BoolVar(&cfg.TestMetrics, "test-metrics", false, "Test mode: gather and output metrics to stdout, then exit")

	flag.Parse()

	cfg.HeartbeatInterval = time.Duration(intervalSec) * time.Second

	// Ensure HubURL has scheme
	if !cfg.TestMetrics && cfg.HubURL == "" {
		return nil, fmt.Errorf("--hub-url is required")
	}

	// Resolve or load NodeID if not provided
	if cfg.NodeID == "" {
		cfg.NodeID = resolveNodeID()
	}

	return cfg, nil
}

func resolveNodeID() string {
	// 1. Check /etc/controlplane/node-id
	nodeIDFile := "/etc/controlplane/node-id"
	if data, err := os.ReadFile(nodeIDFile); err == nil {
		id := strings.TrimSpace(string(data))
		if id != "" {
			return id
		}
	}

	// 2. Check /etc/machine-id
	if data, err := os.ReadFile("/etc/machine-id"); err == nil {
		id := strings.TrimSpace(string(data))
		if id != "" {
			return id
		}
	}

	// 3. Fallback to hostname
	if h, err := os.Hostname(); err == nil && h != "" {
		return h
	}

	return "default-node"
}

func SaveNodeID(nodeID string) error {
	dir := "/etc/controlplane"
	if err := os.MkdirAll(dir, 0755); err != nil {
		return err
	}
	return os.WriteFile(filepath.Join(dir, "node-id"), []byte(nodeID), 0644)
}

func getEnv(key, fallback string) string {
	if val := os.Getenv(key); val != "" {
		return val
	}
	return fallback
}
