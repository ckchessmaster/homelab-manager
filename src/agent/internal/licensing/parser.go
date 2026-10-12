package licensing

import (
	"encoding/json"
	"fmt"
	"strconv"
	"strings"
	"time"
)

type licenseJsonRaw struct {
	Name               string      `json:"Name"`
	Description        string      `json:"Description"`
	LicenseStatus      interface{} `json:"LicenseStatus"`
	GracePeriodMinutes interface{} `json:"GracePeriodMinutes"`
	PartialProductKey  string      `json:"PartialProductKey"`
	RearmsRemaining    interface{} `json:"RearmsRemaining"`
	Error              string      `json:"Error,omitempty"`
}

func parseWindowsLicenseJson(raw []byte, now time.Time) (*WindowsLicenseSummary, error) {
	trimmed := strings.TrimSpace(string(raw))
	if trimmed == "" {
		return &WindowsLicenseSummary{
			CollectedAt: now.Format(time.RFC3339),
			Error:       "Empty output received from PowerShell",
		}, nil
	}

	var data licenseJsonRaw
	if err := json.Unmarshal([]byte(trimmed), &data); err != nil {
		return &WindowsLicenseSummary{
			CollectedAt: now.Format(time.RFC3339),
			Error:       fmt.Sprintf("Failed to parse license JSON: %v", err),
		}, nil
	}

	if data.Error != "" {
		return &WindowsLicenseSummary{
			CollectedAt: now.Format(time.RFC3339),
			Error:       data.Error,
		}, nil
	}

	statusInt := parseInt(data.LicenseStatus)
	graceMinutes := parseInt64(data.GracePeriodMinutes)
	rearms := int(parseInt(data.RearmsRemaining))

	statusName := mapLicenseStatus(statusInt)
	isEval := strings.Contains(strings.ToLower(data.Name), "eval") ||
		strings.Contains(strings.ToLower(data.Description), "timebased_eval") ||
		strings.Contains(strings.ToLower(data.Description), "eval") ||
		statusInt == 2 || statusInt == 3

	days := int(graceMinutes / (60 * 24))
	hours := int(graceMinutes / 60)

	var expirationStr string
	if graceMinutes > 0 {
		expirationStr = now.Add(time.Duration(graceMinutes) * time.Minute).Format(time.RFC3339)
	}

	// Warning when <= 14 days; Critical when <= 3 days or rearms == 0
	isExpiringSoon := graceMinutes > 0 && days <= 14
	isCritical := (graceMinutes > 0 && days <= 3) || rearms == 0

	edition := strings.TrimSpace(data.Name)
	if edition == "" {
		edition = "Windows Server"
	}

	return &WindowsLicenseSummary{
		IsEvaluation:       isEval,
		Edition:            edition,
		Description:        strings.TrimSpace(data.Description),
		LicenseStatus:      statusName,
		GracePeriodMinutes: graceMinutes,
		DaysRemaining:      days,
		HoursRemaining:     hours,
		ExpirationDate:     expirationStr,
		RearmsRemaining:    rearms,
		PartialProductKey:  strings.TrimSpace(data.PartialProductKey),
		IsExpiringSoon:     isExpiringSoon,
		IsCritical:         isCritical,
		CollectedAt:        now.Format(time.RFC3339),
	}, nil
}

func mapLicenseStatus(status int64) string {
	switch status {
	case 0:
		return "Unlicensed"
	case 1:
		return "Licensed"
	case 2:
		return "OOBGrace"
	case 3:
		return "OOTGrace"
	case 4:
		return "NonGenuineGrace"
	case 5:
		return "Notification"
	case 6:
		return "ExtendedGrace"
	default:
		return "Unknown"
	}
}

func parseInt(v interface{}) int64 {
	switch val := v.(type) {
	case float64:
		return int64(val)
	case int:
		return int64(val)
	case int64:
		return val
	case string:
		if i, err := strconv.ParseInt(val, 10, 64); err == nil {
			return i
		}
	}
	return 0
}

func parseInt64(v interface{}) int64 {
	return parseInt(v)
}
