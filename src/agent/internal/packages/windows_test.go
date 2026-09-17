package packages

import (
	"testing"
)

func TestParseWindowsUpdateJson_Success(t *testing.T) {
	jsonPayload := `{"total":5,"security":2}`
	summary, err := parseWindowsUpdateJson([]byte(jsonPayload))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if summary.PackageManager != "windows_update" {
		t.Errorf("expected packageManager to be 'windows_update', got '%s'", summary.PackageManager)
	}
	if summary.UpgradableCount != 5 {
		t.Errorf("expected UpgradableCount to be 5, got %d", summary.UpgradableCount)
	}
	if summary.SecurityCount != 2 {
		t.Errorf("expected SecurityCount to be 2, got %d", summary.SecurityCount)
	}
}

func TestParseWindowsUpdateJson_WithError(t *testing.T) {
	jsonPayload := `{"total":0,"security":0,"error":"The service cannot be started"}`
	summary, err := parseWindowsUpdateJson([]byte(jsonPayload))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if summary.UpgradableCount != 0 || summary.SecurityCount != 0 {
		t.Errorf("expected 0 counts on error, got upgradable=%d, security=%d", summary.UpgradableCount, summary.SecurityCount)
	}
}

func TestParseWindowsUpdateJson_Empty(t *testing.T) {
	summary, err := parseWindowsUpdateJson([]byte(""))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if summary.UpgradableCount != 0 {
		t.Errorf("expected 0 count for empty output, got %d", summary.UpgradableCount)
	}
}
