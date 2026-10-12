//go:build windows

package licensing

import (
	"context"
	"os/exec"
	"time"
)

type windowsInspector struct {
	powerShellPath string
}

func newPlatformInspector() Inspector {
	path := "powershell.exe"
	if p, err := exec.LookPath("powershell.exe"); err == nil {
		path = p
	} else if p, err := exec.LookPath("pwsh.exe"); err == nil {
		path = p
	}
	return &windowsInspector{powerShellPath: path}
}

type winLicenseRaw struct {
	Name               string      `json:"Name"`
	Description        string      `json:"Description"`
	LicenseStatus      interface{} `json:"LicenseStatus"`
	GracePeriodMinutes interface{} `json:"GracePeriodMinutes"`
	PartialProductKey  string      `json:"PartialProductKey"`
	RearmsRemaining    interface{} `json:"RearmsRemaining"`
	Error              string      `json:"Error,omitempty"`
}

func (wi *windowsInspector) Inspect(ctx context.Context) (*WindowsLicenseSummary, error) {
	cmdCtx, cancel := context.WithTimeout(ctx, 30*time.Second)
	defer cancel()

	psScript := `$ErrorActionPreference = 'Stop'
try {
    $service = Get-CimInstance -ClassName SoftwareLicensingService -ErrorAction SilentlyContinue
    $rearms = if ($service) { $service.RemainingWindowsReArmCount } else { 0 }
    
    $prod = Get-CimInstance -ClassName SoftwareLicensingProduct -Filter "PartialProductKey IS NOT NULL and Name LIKE 'Windows%'" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $prod) {
        $prod = Get-CimInstance -ClassName SoftwareLicensingProduct -Filter "PartialProductKey IS NOT NULL" -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    
    if (-not $prod) {
        [PSCustomObject]@{
            Name = "Windows Operating System"
            Description = ""
            LicenseStatus = 0
            GracePeriodMinutes = 0
            PartialProductKey = ""
            RearmsRemaining = $rearms
        } | ConvertTo-Json -Compress
        return
    }

    [PSCustomObject]@{
        Name = $prod.Name
        Description = $prod.Description
        LicenseStatus = $prod.LicenseStatus
        GracePeriodMinutes = $prod.GracePeriodRemaining
        PartialProductKey = $prod.PartialProductKey
        RearmsRemaining = $rearms
    } | ConvertTo-Json -Compress
} catch {
    [PSCustomObject]@{
        Name = ""
        Description = ""
        LicenseStatus = 0
        GracePeriodMinutes = 0
        PartialProductKey = ""
        RearmsRemaining = 0
        Error = $_.Exception.Message
    } | ConvertTo-Json -Compress
}`

	cmd := exec.CommandContext(cmdCtx, wi.powerShellPath, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", psScript)
	out, err := cmd.Output()
	if err != nil {
		return &WindowsLicenseSummary{
			CollectedAt: time.Now().UTC().Format(time.RFC3339),
			Error:       err.Error(),
		}, nil
	}

	return parseWindowsLicenseJson(out, time.Now().UTC())
}
