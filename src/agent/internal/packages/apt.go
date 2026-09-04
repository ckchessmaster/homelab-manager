package packages

import (
	"bufio"
	"bytes"
	"context"
	"os/exec"
	"strings"
	"time"
)

type AptInspector struct{}

func NewAptInspector() *AptInspector {
	return &AptInspector{}
}

func (a *AptInspector) Name() string {
	return "apt"
}

func (a *AptInspector) Inspect(ctx context.Context) (*PackageSummary, error) {
	ctx, cancel := context.WithTimeout(ctx, 15*time.Second)
	defer cancel()

	// apt-get -s upgrade runs a simulation without requiring root or taking locks
	cmd := exec.CommandContext(ctx, "apt-get", "-s", "upgrade")
	out, err := cmd.Output()
	if err != nil {
		// Fallback to apt list --upgradable
		return a.inspectFallback(ctx)
	}

	summary := &PackageSummary{
		PackageManager: "apt",
	}

	scanner := bufio.NewScanner(bytes.NewReader(out))
	for scanner.Scan() {
		line := scanner.Text()
		// Lines starting with 'Inst ' represent an upgradable package
		if strings.HasPrefix(line, "Inst ") {
			summary.UpgradableCount++
			lower := strings.ToLower(line)
			if strings.Contains(lower, "security") || strings.Contains(lower, "-sec") {
				summary.SecurityCount++
			}
		}
	}

	return summary, nil
}

func (a *AptInspector) inspectFallback(ctx context.Context) (*PackageSummary, error) {
	cmd := exec.CommandContext(ctx, "apt", "list", "--upgradable")
	out, err := cmd.Output()
	if err != nil {
		return &PackageSummary{PackageManager: "apt"}, nil
	}

	summary := &PackageSummary{
		PackageManager: "apt",
	}

	scanner := bufio.NewScanner(bytes.NewReader(out))
	for scanner.Scan() {
		line := scanner.Text()
		if strings.HasPrefix(line, "Listing...") || strings.TrimSpace(line) == "" {
			continue
		}
		summary.UpgradableCount++
		if strings.Contains(strings.ToLower(line), "security") {
			summary.SecurityCount++
		}
	}

	return summary, nil
}
