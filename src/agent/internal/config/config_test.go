package config

import (
	"reflect"
	"testing"
)

func TestGetHubURLs(t *testing.T) {
	tests := []struct {
		name     string
		cfg      Config
		expected []string
	}{
		{
			name: "primary only",
			cfg: Config{
				HubURL: "ws://cluster.local/agent-hub",
			},
			expected: []string{"ws://cluster.local/agent-hub"},
		},
		{
			name: "primary and standby",
			cfg: Config{
				HubURL:        "ws://cluster.local/agent-hub",
				StandbyHubURL: "ws://workstation.local:5200/agent-hub",
			},
			expected: []string{
				"ws://cluster.local/agent-hub",
				"ws://workstation.local:5200/agent-hub",
			},
		},
		{
			name: "duplicate standby ignored",
			cfg: Config{
				HubURL:        "ws://cluster.local/agent-hub",
				StandbyHubURL: "ws://cluster.local/agent-hub",
			},
			expected: []string{"ws://cluster.local/agent-hub"},
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got := tt.cfg.GetHubURLs()
			if !reflect.DeepEqual(got, tt.expected) {
				t.Errorf("GetHubURLs() = %v, want %v", got, tt.expected)
			}
		})
	}
}
