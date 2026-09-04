package metrics

import (
	"testing"
)

func TestCollector_Collect(t *testing.T) {
	collector := NewCollector()
	m, err := collector.Collect()
	if err != nil {
		t.Fatalf("failed to collect metrics: %v", err)
	}

	if m.CPUUsagePct < 0 || m.CPUUsagePct > 100 {
		t.Errorf("CPU usage out of bounds [0, 100]: %f", m.CPUUsagePct)
	}
	if m.MemoryUsagePct < 0 || m.MemoryUsagePct > 100 {
		t.Errorf("Memory usage out of bounds [0, 100]: %f", m.MemoryUsagePct)
	}
	if m.DiskFreePct < 0 || m.DiskFreePct > 100 {
		t.Errorf("Disk free out of bounds [0, 100]: %f", m.DiskFreePct)
	}

	kernel := collector.KernelVersion()
	if kernel == "" {
		t.Errorf("kernel version is empty")
	}
}
