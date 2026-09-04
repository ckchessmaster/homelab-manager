//go:build linux

package metrics

import (
	"bufio"
	"bytes"
	"math"
	"os"
	"os/exec"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"time"
)

type linuxCollector struct {
	mu          sync.Mutex
	prevIdle    uint64
	prevTotal   uint64
	hasPrevStat bool
}

func newDefaultCollector() Collector {
	return newLinuxCollector()
}

func newLinuxCollector() Collector {
	return &linuxCollector{}
}

func (c *linuxCollector) Collect() (*Metrics, error) {
	cpuPct := c.getCPUUsage()
	memPct := c.getMemoryUsage()
	diskPct := c.getDiskFree()

	return &Metrics{
		CPUUsagePct:    round(cpuPct, 1),
		MemoryUsagePct: round(memPct, 1),
		DiskFreePct:    round(diskPct, 1),
	}, nil
}

func (c *linuxCollector) getCPUUsage() float64 {
	c.mu.Lock()
	defer c.mu.Unlock()

	idle, total, err := readProcStat()
	if err != nil {
		return 0.0
	}

	if !c.hasPrevStat {
		c.prevIdle = idle
		c.prevTotal = total
		c.hasPrevStat = true
		// Short sleep on first read to establish a delta
		time.Sleep(100 * time.Millisecond)
		idle, total, err = readProcStat()
		if err != nil {
			return 0.0
		}
	}

	totalDelta := total - c.prevTotal
	idleDelta := idle - c.prevIdle

	c.prevIdle = idle
	c.prevTotal = total

	if totalDelta == 0 {
		return 0.0
	}

	cpuUsage := (1.0 - float64(idleDelta)/float64(totalDelta)) * 100.0
	if cpuUsage < 0 {
		cpuUsage = 0
	}
	if cpuUsage > 100 {
		cpuUsage = 100
	}
	return cpuUsage
}

func readProcStat() (idle, total uint64, err error) {
	data, err := os.ReadFile("/proc/stat")
	if err != nil {
		return 0, 0, err
	}

	scanner := bufio.NewScanner(bytes.NewReader(data))
	for scanner.Scan() {
		line := scanner.Text()
		if strings.HasPrefix(line, "cpu ") {
			fields := strings.Fields(line)[1:]
			var sum uint64
			for i, f := range fields {
				val, _ := strconv.ParseUint(f, 10, 64)
				sum += val
				if i == 3 || i == 4 { // idle or iowait
					idle += val
				}
			}
			return idle, sum, nil
		}
	}
	return 0, 0, nil
}

func (c *linuxCollector) getMemoryUsage() float64 {
	data, err := os.ReadFile("/proc/meminfo")
	if err != nil {
		return 0.0
	}

	var memTotal, memAvailable uint64
	scanner := bufio.NewScanner(bytes.NewReader(data))
	for scanner.Scan() {
		line := scanner.Text()
		parts := strings.Fields(line)
		if len(parts) < 2 {
			continue
		}
		if parts[0] == "MemTotal:" {
			memTotal, _ = strconv.ParseUint(parts[1], 10, 64)
		} else if parts[0] == "MemAvailable:" {
			memAvailable, _ = strconv.ParseUint(parts[1], 10, 64)
		}
	}

	if memTotal == 0 {
		return 0.0
	}
	used := memTotal - memAvailable
	return (float64(used) / float64(memTotal)) * 100.0
}

func (c *linuxCollector) getDiskFree() float64 {
	var stat syscall.Statfs_t
	if err := syscall.Statfs("/", &stat); err != nil {
		return 0.0
	}

	if stat.Blocks == 0 {
		return 0.0
	}

	freePct := (float64(stat.Bavail) / float64(stat.Blocks)) * 100.0
	return freePct
}

func (c *linuxCollector) IsRebootRequired() bool {
	// Debian / Ubuntu indicator
	if _, err := os.Stat("/var/run/reboot-required"); err == nil {
		return true
	}
	if _, err := os.Stat("/run/reboot-required"); err == nil {
		return true
	}

	// RHEL / CentOS indicator: needs-restarting -r exits with 1 if reboot is needed
	if _, err := exec.LookPath("needs-restarting"); err == nil {
		cmd := exec.Command("needs-restarting", "-r")
		if err := cmd.Run(); err != nil {
			// Exit code 1 means reboot needed
			if exitErr, ok := err.(*exec.ExitError); ok && exitErr.ExitCode() == 1 {
				return true
			}
		}
	}

	return false
}

func (c *linuxCollector) KernelVersion() string {
	if data, err := os.ReadFile("/proc/sys/kernel/osrelease"); err == nil {
		return strings.TrimSpace(string(data))
	}
	return "unknown-linux"
}

func round(val float64, precision int) float64 {
	ratio := math.Pow(10, float64(precision))
	return math.Round(val*ratio) / ratio
}
