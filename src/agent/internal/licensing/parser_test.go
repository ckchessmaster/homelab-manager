package licensing

import (
	"testing"
	"time"
)

func TestParseWindowsLicenseJson_EvaluationExpiringSoon(t *testing.T) {
	raw := `{
		"Name": "Windows(R), ServerStandardEval edition",
		"Description": "Windows Operating System, TIMEBASED_EVAL channel",
		"LicenseStatus": 2,
		"GracePeriodMinutes": 14400,
		"PartialProductKey": "ABCDE",
		"RearmsRemaining": 3
	}`

	now := time.Date(2026, 10, 12, 12, 0, 0, 0, time.UTC)
	res, err := parseWindowsLicenseJson([]byte(raw), now)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if !res.IsEvaluation {
		t.Errorf("expected IsEvaluation=true, got false")
	}
	if res.DaysRemaining != 10 {
		t.Errorf("expected 10 days remaining, got %d", res.DaysRemaining)
	}
	if !res.IsExpiringSoon {
		t.Errorf("expected IsExpiringSoon=true, got false")
	}
	if res.IsCritical {
		t.Errorf("expected IsCritical=false, got true")
	}
	if res.RearmsRemaining != 3 {
		t.Errorf("expected 3 rearms, got %d", res.RearmsRemaining)
	}
	if res.PartialProductKey != "ABCDE" {
		t.Errorf("expected ABCDE, got %s", res.PartialProductKey)
	}
}

func TestParseWindowsLicenseJson_Critical(t *testing.T) {
	// 2 days remaining -> 2880 minutes
	raw := `{
		"Name": "Windows(R), ServerDatacenterEval edition",
		"Description": "Windows Operating System, TIMEBASED_EVAL channel",
		"LicenseStatus": 2,
		"GracePeriodMinutes": 2880,
		"PartialProductKey": "WXYZ9",
		"RearmsRemaining": 1
	}`

	now := time.Date(2026, 10, 12, 12, 0, 0, 0, time.UTC)
	res, err := parseWindowsLicenseJson([]byte(raw), now)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if !res.IsExpiringSoon {
		t.Errorf("expected IsExpiringSoon=true, got false")
	}
	if !res.IsCritical {
		t.Errorf("expected IsCritical=true, got false")
	}
}

func TestParseWindowsLicenseJson_ZeroRearms(t *testing.T) {
	raw := `{
		"Name": "Windows(R), ServerStandardEval edition",
		"Description": "Windows Operating System, TIMEBASED_EVAL channel",
		"LicenseStatus": 2,
		"GracePeriodMinutes": 50000,
		"PartialProductKey": "ABCDE",
		"RearmsRemaining": 0
	}`

	now := time.Date(2026, 10, 12, 12, 0, 0, 0, time.UTC)
	res, err := parseWindowsLicenseJson([]byte(raw), now)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if !res.IsCritical {
		t.Errorf("expected IsCritical=true due to 0 rearms remaining, got false")
	}
}

func TestParseWindowsLicenseJson_Permanent(t *testing.T) {
	raw := `{
		"Name": "Windows(R), ServerStandard edition",
		"Description": "Windows Operating System, RETAIL channel",
		"LicenseStatus": 1,
		"GracePeriodMinutes": 0,
		"PartialProductKey": "FINAL",
		"RearmsRemaining": 4
	}`

	now := time.Date(2026, 10, 12, 12, 0, 0, 0, time.UTC)
	res, err := parseWindowsLicenseJson([]byte(raw), now)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if res.IsEvaluation {
		t.Errorf("expected IsEvaluation=false, got true")
	}
	if res.IsExpiringSoon || res.IsCritical {
		t.Errorf("expected not expiring soon / not critical, got isExpiringSoon=%v, isCritical=%v", res.IsExpiringSoon, res.IsCritical)
	}
	if res.LicenseStatus != "Licensed" {
		t.Errorf("expected Licensed, got %s", res.LicenseStatus)
	}
}
