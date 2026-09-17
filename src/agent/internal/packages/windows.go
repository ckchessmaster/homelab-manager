package packages

import (
	"context"
	"encoding/json"
	"log"
	"os/exec"
	"strings"
	"time"
)

type WindowsUpdateInspector struct {
	powerShellPath string
}

func NewWindowsUpdateInspector() *WindowsUpdateInspector {
	path := "powershell.exe"
	if p, err := exec.LookPath("powershell.exe"); err == nil {
		path = p
	} else if p, err := exec.LookPath("pwsh.exe"); err == nil {
		path = p
	} else if p, err := exec.LookPath("pwsh"); err == nil {
		path = p
	}
	return &WindowsUpdateInspector{powerShellPath: path}
}

func (w *WindowsUpdateInspector) Name() string {
	return "windows_update"
}

type windowsUpdateResult struct {
	Total    int    `json:"total"`
	Security int    `json:"security"`
	Error    string `json:"error,omitempty"`
}

func (w *WindowsUpdateInspector) Inspect(ctx context.Context) (*PackageSummary, error) {
	psScript := `$ErrorActionPreference = 'Stop'
try {
    $session = New-Object -ComObject Microsoft.Update.Session
    $searcher = $session.CreateUpdateSearcher()
    $res = $searcher.Search("IsInstalled=0 and Type='Software' and IsHidden=0")
    $total = $res.Updates.Count
    $sec = 0
    foreach ($u in $res.Updates) {
        $isSec = $false
        if ($u.MsrcSeverity -and $u.MsrcSeverity -ne "") {
            $isSec = $true
        } else {
            foreach ($cat in $u.Categories) {
                if ($cat.Name -like "*Security*" -or $cat.Name -like "*Critical*") {
                    $isSec = $true
                    break
                }
            }
        }
        if ($isSec) { $sec++ }
    }
    [PSCustomObject]@{ total = $total; security = $sec } | ConvertTo-Json -Compress
} catch {
    [PSCustomObject]@{ total = 0; security = 0; error = $_.Exception.Message } | ConvertTo-Json -Compress
}`

	cmdCtx, cancel := context.WithTimeout(ctx, 45*time.Second)
	defer cancel()

	cmd := exec.CommandContext(cmdCtx, w.powerShellPath, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", psScript)
	out, err := cmd.Output()
	if err != nil {
		log.Printf("[Agent] Windows update inspection PowerShell execution returned: %v", err)
		return &PackageSummary{
			PackageManager:  "windows_update",
			UpgradableCount: 0,
			SecurityCount:   0,
		}, nil
	}

	return parseWindowsUpdateJson(out)
}

func parseWindowsUpdateJson(raw []byte) (*PackageSummary, error) {
	trimmed := strings.TrimSpace(string(raw))
	if trimmed == "" {
		return &PackageSummary{
			PackageManager:  "windows_update",
			UpgradableCount: 0,
			SecurityCount:   0,
		}, nil
	}

	var res windowsUpdateResult
	if err := json.Unmarshal([]byte(trimmed), &res); err != nil {
		log.Printf("[Agent] Failed to parse Windows update output: %v (output: %s)", err, trimmed)
		return &PackageSummary{
			PackageManager:  "windows_update",
			UpgradableCount: 0,
			SecurityCount:   0,
		}, nil
	}

	if res.Error != "" {
		log.Printf("[Agent] Windows Update inspection warning: %s", res.Error)
	}

	return &PackageSummary{
		PackageManager:  "windows_update",
		UpgradableCount: res.Total,
		SecurityCount:   res.Security,
	}, nil
}
