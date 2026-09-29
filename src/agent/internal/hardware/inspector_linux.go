//go:build linux

package hardware

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

type linuxInspector struct{}

func newPlatformInspector() Inspector {
	return &linuxInspector{}
}

func (li *linuxInspector) Inspect(ctx context.Context) (*HardwareSummary, error) {
	start := time.Now()
	summary := &HardwareSummary{
		Disks:       make([]PhysicalDisk, 0),
		CollectedAt: time.Now().UTC().Format(time.RFC3339),
	}

	entries, err := os.ReadDir("/sys/block")
	if err != nil {
		summary.ScanDuration = time.Since(start).String()
		return summary, nil
	}

	for _, entry := range entries {
		select {
		case <-ctx.Done():
			return summary, ctx.Err()
		default:
		}

		name := entry.Name()
		if isIgnoredBlockDevice(name) {
			continue
		}

		disk := inspectLinuxDisk(ctx, name)
		if disk != nil {
			summary.Disks = append(summary.Disks, *disk)
		}
	}

	summary.ScanDuration = time.Since(start).String()
	return summary, nil
}

func isIgnoredBlockDevice(name string) bool {
	ignoredPrefixes := []string{"loop", "ram", "dm-", "zram", "sr", "nbd", "zd"}
	for _, p := range ignoredPrefixes {
		if strings.HasPrefix(name, p) {
			return true
		}
	}
	return false
}

func inspectLinuxDisk(ctx context.Context, name string) *PhysicalDisk {
	sysPath := filepath.Join("/sys/block", name)
	devPath := filepath.Join("/dev", name)

	// Read device size in 512-byte blocks
	var sizeBytes int64
	if sizeData, err := os.ReadFile(filepath.Join(sysPath, "size")); err == nil {
		if sectors, err := strconv.ParseInt(strings.TrimSpace(string(sizeData)), 10, 64); err == nil {
			sizeBytes = sectors * 512
		}
	}

	// Filter out 0-byte pseudo block devices
	if sizeBytes <= 0 {
		return nil
	}

	// Determine rotational state (0 = SSD/NVMe, 1 = HDD)
	mediaType := "HDD"
	if rotData, err := os.ReadFile(filepath.Join(sysPath, "queue", "rotational")); err == nil {
		if strings.TrimSpace(string(rotData)) == "0" {
			mediaType = "SSD"
		}
	}
	if strings.HasPrefix(name, "nvme") {
		mediaType = "NVMe"
	}

	// Read model and serial from sysfs if available
	var model, serial string
	if mData, err := os.ReadFile(filepath.Join(sysPath, "device", "model")); err == nil {
		model = strings.TrimSpace(string(mData))
	}
	if sData, err := os.ReadFile(filepath.Join(sysPath, "device", "serial")); err == nil {
		serial = strings.TrimSpace(string(sData))
	}

	disk := &PhysicalDisk{
		DeviceID:          devPath,
		Name:              model,
		Model:             model,
		SerialNumber:      serial,
		MediaType:         mediaType,
		SizeBytes:         sizeBytes,
		Status:            "Ok",
		SmartHealthStatus: "PASSED",
		Attributes:        make(map[string]string),
	}

	// Enrich with smartctl if available
	if hasSmartctl() {
		enrichWithSmartctl(ctx, devPath, disk)
	} else if disk.MediaType == "NVMe" && hasNvmeCli() {
		enrichWithNvmeCli(ctx, devPath, disk)
	}

	return disk
}

func hasSmartctl() bool {
	_, err := exec.LookPath("smartctl")
	return err == nil
}

func hasNvmeCli() bool {
	_, err := exec.LookPath("nvme")
	return err == nil
}

func enrichWithSmartctl(ctx context.Context, devPath string, disk *PhysicalDisk) {
	cmdCtx, cancel := context.WithTimeout(ctx, 4*time.Second)
	defer cancel()

	// Run smartctl with JSON output
	cmd := exec.CommandContext(cmdCtx, "smartctl", "--json=c", "-a", devPath)
	out, err := cmd.Output()
	if err != nil && len(out) == 0 {
		return
	}

	var root map[string]interface{}
	if err := json.Unmarshal(out, &root); err != nil {
		return
	}

	// Model / Serial fallbacks from smartctl if sysfs was blank
	if disk.Model == "" {
		if m, ok := root["model_name"].(string); ok && m != "" {
			disk.Model = strings.TrimSpace(m)
			disk.Name = disk.Model
		}
	}
	if disk.SerialNumber == "" {
		if s, ok := root["serial_number"].(string); ok && s != "" {
			disk.SerialNumber = strings.TrimSpace(s)
		}
	}

	// Overall SMART status
	if smartStatus, ok := root["smart_status"].(map[string]interface{}); ok {
		if passed, ok := smartStatus["passed"].(bool); ok {
			if !passed {
				disk.SmartHealthStatus = "FAILED"
				disk.Status = "Critical"
			} else {
				disk.SmartHealthStatus = "PASSED"
			}
		}
	}

	// Temperature
	if tempObj, ok := root["temperature"].(map[string]interface{}); ok {
		if curr, ok := tempObj["current"].(float64); ok && curr > 0 {
			disk.TemperatureC = &curr
			if curr >= 65.0 {
				disk.Status = "Critical"
			} else if curr >= 55.0 && disk.Status == "Ok" {
				disk.Status = "Warning"
			}
		}
	}

	// NVMe smart health log (percentage used)
	if nvmeLog, ok := root["nvme_smart_health_information_log"].(map[string]interface{}); ok {
		if pctUsed, ok := nvmeLog["percentage_used"].(float64); ok {
			remaining := 100.0 - pctUsed
			if remaining < 0 {
				remaining = 0
			}
			disk.WearOutPercentage = &remaining
			if remaining <= 2.0 {
				disk.Status = "Critical"
			} else if remaining <= 10.0 && disk.Status == "Ok" {
				disk.Status = "Warning"
			}
		}
	}

	// ATA SMART attributes table (SSD life / wear indicators)
	if ataObj, ok := root["ata_smart_attributes"].(map[string]interface{}); ok {
		if tbl, ok := ataObj["table"].([]interface{}); ok {
			for _, item := range tbl {
				if attr, ok := item.(map[string]interface{}); ok {
					id, okId := attr["id"].(float64)
					name, _ := attr["name"].(string)
					// Common wear indicator IDs: 231 (SSD Life Left), 177 (Wear Range Delta), 202 (Percent Lifetime Remaining), 169 (Bad Block Count)
					if okId && (id == 231 || id == 202 || id == 177) {
						if val, ok := attr["value"].(float64); ok && val >= 0 && val <= 100 {
							disk.WearOutPercentage = &val
							if val <= 2.0 {
								disk.Status = "Critical"
							} else if val <= 10.0 && disk.Status == "Ok" {
								disk.Status = "Warning"
							}
						}
					}
					if name != "" {
						if raw, ok := attr["raw"].(map[string]interface{}); ok {
							if str, ok := raw["string"].(string); ok {
								disk.Attributes[name] = str
							}
						}
					}
				}
			}
		}
	}
}

func enrichWithNvmeCli(ctx context.Context, devPath string, disk *PhysicalDisk) {
	cmdCtx, cancel := context.WithTimeout(ctx, 3*time.Second)
	defer cancel()

	cmd := exec.CommandContext(cmdCtx, "nvme", "smart-log", devPath, "-o", "json")
	out, err := cmd.Output()
	if err != nil || len(out) == 0 {
		return
	}

	var root map[string]interface{}
	if err := json.Unmarshal(out, &root); err != nil {
		return
	}

	if pctUsed, ok := root["percent_used"].(float64); ok {
		remaining := 100.0 - pctUsed
		if remaining < 0 {
			remaining = 0
		}
		disk.WearOutPercentage = &remaining
	}

	if tempK, ok := root["temperature"].(float64); ok && tempK > 100 {
		// Convert Kelvin to Celsius
		celsius := tempK - 273.15
		disk.TemperatureC = &celsius
	} else if tempC, ok := root["temperature"].(float64); ok && tempC > 0 && tempC <= 120 {
		disk.TemperatureC = &tempC
	}

	if critWarn, ok := root["critical_warning"].(float64); ok && critWarn != 0 {
		disk.SmartHealthStatus = fmt.Sprintf("WARNING_0x%X", int(critWarn))
		disk.Status = "Critical"
	}
}
