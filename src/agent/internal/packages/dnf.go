package packages

import (
	"bufio"
	"bytes"
	"context"
	"os/exec"
	"strings"
	"time"
)

type DnfInspector struct{}

func NewDnfInspector() *DnfInspector {
	return &DnfInspector{}
}

func (d *DnfInspector) Name() string {
	return "dnf"
}

func (d *DnfInspector) Inspect(ctx context.Context) (*PackageSummary, error) {
	ctx, cancel := context.WithTimeout(ctx, 15*time.Second)
	defer cancel()

	summary := &PackageSummary{
		PackageManager: "dnf",
	}

	// dnf check-update exits with 100 if updates are available, 0 if no updates, 1 on error
	cmd := exec.CommandContext(ctx, "dnf", "check-update", "-q")
	out, err := cmd.Output()
	if err != nil {
		if exitErr, ok := err.(*exec.ExitError); ok && exitErr.ExitCode() == 100 {
			// Expected returncode when updates are available
		} else {
			return summary, nil
		}
	}

	scanner := bufio.NewScanner(bytes.NewReader(out))
	for scanner.Scan() {
		line := strings.TrimSpace(scanner.Text())
		if line == "" || strings.HasPrefix(line, "Security:") {
			continue
		}
		fields := strings.Fields(line)
		if len(fields) >= 2 {
			summary.UpgradableCount++
		}
	}

	// Security updates check
	secCmd := exec.CommandContext(ctx, "dnf", "updateinfo", "-q", "--security", "summary")
	if secOut, err := secCmd.Output(); err == nil {
		secScanner := bufio.NewScanner(bytes.NewReader(secOut))
		for secScanner.Scan() {
			l := strings.ToLower(secScanner.Text())
			if strings.Contains(l, "security notice") || strings.Contains(l, "security update") {
				summary.SecurityCount++
			}
		}
	}

	return summary, nil
}
