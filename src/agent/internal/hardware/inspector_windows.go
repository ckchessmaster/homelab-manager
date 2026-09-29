//go:build windows

package hardware

import (
	"context"
	"encoding/json"
	"os/exec"
	"strconv"
	"strings"
	"time"
)

type windowsInspector struct{}

func newPlatformInspector() Inspector {
	return &windowsInspector{}
}

type winDiskRaw struct {
	DeviceID          interface{} `json:"DeviceId"`
	FriendlyName      string      `json:"FriendlyName"`
	Model             string      `json:"Model"`
	SerialNumber      string      `json:"SerialNumber"`
	MediaType         interface{} `json:"MediaType"`
	Size              interface{} `json:"Size"`
	OperationalStatus interface{} `json:"OperationalStatus"`
	HealthStatus      interface{} `json:"HealthStatus"`
}

func (wi *windowsInspector) Inspect(ctx context.Context) (*HardwareSummary, error) {
	start := time.Now()
	summary := &HardwareSummary{
		Disks:       make([]PhysicalDisk, 0),
		CollectedAt: time.Now().UTC().Format(time.RFC3339),
	}

	cmdCtx, cancel := context.WithTimeout(ctx, 10*time.Second)
	defer cancel()

	psScript := `Get-CimInstance -ClassName MSFT_PhysicalDisk -Namespace ROOT/Microsoft/Windows/Storage | Select-Object DeviceId, FriendlyName, Model, SerialNumber, MediaType, Size, OperationalStatus, HealthStatus | ConvertTo-Json -Compress`
	cmd := exec.CommandContext(cmdCtx, "powershell", "-NoProfile", "-NonInteractive", "-Command", psScript)
	out, err := cmd.Output()
	if err != nil || len(out) == 0 {
		// Fallback to Win32_DiskDrive if Storage namespace is unavailable
		summary.Disks = inspectWin32DiskDrive(ctx)
		summary.ScanDuration = time.Since(start).String()
		return summary, nil
	}

	// Output might be a single object or an array of objects
	var disks []winDiskRaw
	trimmed := strings.TrimSpace(string(out))
	if strings.HasPrefix(trimmed, "[") {
		_ = json.Unmarshal([]byte(trimmed), &disks)
	} else if strings.HasPrefix(trimmed, "{") {
		var single winDiskRaw
		if err := json.Unmarshal([]byte(trimmed), &single); err == nil {
			disks = append(disks, single)
		}
	}

	for _, d := range disks {
		devID := formatWinDevID(d.DeviceID)
		mediaType := formatWinMediaType(d.MediaType)
		sizeBytes := parseWinInt64(d.Size)
		status := formatWinHealthStatus(d.HealthStatus)

		model := strings.TrimSpace(d.Model)
		if model == "" {
			model = strings.TrimSpace(d.FriendlyName)
		}

		summary.Disks = append(summary.Disks, PhysicalDisk{
			DeviceID:          devID,
			Name:              d.FriendlyName,
			Model:             model,
			SerialNumber:      strings.TrimSpace(d.SerialNumber),
			MediaType:         mediaType,
			SizeBytes:         sizeBytes,
			Status:            status,
			SmartHealthStatus: status,
			Attributes:        make(map[string]string),
		})
	}

	summary.ScanDuration = time.Since(start).String()
	return summary, nil
}

func inspectWin32DiskDrive(ctx context.Context) []PhysicalDisk {
	cmdCtx, cancel := context.WithTimeout(ctx, 8*time.Second)
	defer cancel()

	psScript := `Get-CimInstance Win32_DiskDrive | Select-Object DeviceID, Model, SerialNumber, Size, Status | ConvertTo-Json -Compress`
	cmd := exec.CommandContext(cmdCtx, "powershell", "-NoProfile", "-NonInteractive", "-Command", psScript)
	out, err := cmd.Output()
	if err != nil || len(out) == 0 {
		return nil
	}

	type w32Disk struct {
		DeviceID     string      `json:"DeviceID"`
		Model        string      `json:"Model"`
		SerialNumber string      `json:"SerialNumber"`
		Size         interface{} `json:"Size"`
		Status       string      `json:"Status"`
	}

	var rawList []w32Disk
	trimmed := strings.TrimSpace(string(out))
	if strings.HasPrefix(trimmed, "[") {
		_ = json.Unmarshal([]byte(trimmed), &rawList)
	} else if strings.HasPrefix(trimmed, "{") {
		var s w32Disk
		if err := json.Unmarshal([]byte(trimmed), &s); err == nil {
			rawList = append(rawList, s)
		}
	}

	result := make([]PhysicalDisk, 0, len(rawList))
	for _, d := range rawList {
		status := "Ok"
		if strings.ToUpper(d.Status) != "OK" {
			status = "Warning"
		}
		result = append(result, PhysicalDisk{
			DeviceID:          d.DeviceID,
			Name:              d.Model,
			Model:             d.Model,
			SerialNumber:      strings.TrimSpace(d.SerialNumber),
			MediaType:         "Unknown",
			SizeBytes:         parseWinInt64(d.Size),
			Status:            status,
			SmartHealthStatus: d.Status,
			Attributes:        make(map[string]string),
		})
	}
	return result
}

func formatWinDevID(val interface{}) string {
	switch v := val.(type) {
	case string:
		return v
	case float64:
		return strconv.FormatInt(int64(v), 10)
	default:
		return "PhysicalDisk"
	}
}

func formatWinMediaType(val interface{}) string {
	// MSFT_PhysicalDisk MediaType: 3 = HDD, 4 = SSD, 5 = SCM
	switch v := val.(type) {
	case float64:
		switch int(v) {
		case 3:
			return "HDD"
		case 4:
			return "SSD"
		case 5:
			return "NVMe"
		default:
			return "Unknown"
		}
	case string:
		s := strings.ToUpper(v)
		if strings.Contains(s, "SSD") {
			return "SSD"
		}
		if strings.Contains(s, "NVME") {
			return "NVMe"
		}
		if strings.Contains(s, "HDD") {
			return "HDD"
		}
		return v
	default:
		return "Unknown"
	}
}

func parseWinInt64(val interface{}) int64 {
	switch v := val.(type) {
	case float64:
		return int64(v)
	case string:
		n, _ := strconv.ParseInt(v, 10, 64)
		return n
	default:
		return 0
	}
}

func formatWinHealthStatus(val interface{}) string {
	// 0 = Healthy, 1 = Warning, 2 = Unhealthy
	switch v := val.(type) {
	case float64:
		switch int(v) {
		case 0:
			return "Ok"
		case 1:
			return "Warning"
		case 2:
			return "Critical"
		default:
			return "Unknown"
		}
	default:
		return "Ok"
	}
}
