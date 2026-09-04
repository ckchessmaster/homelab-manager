package packages

import (
	"context"
	"os/exec"
)

type PackageSummary struct {
	PackageManager  string `json:"packageManager"`
	UpgradableCount int    `json:"upgradableCount"`
	SecurityCount   int    `json:"securityCount"`
}

type Inspector interface {
	Name() string
	Inspect(ctx context.Context) (*PackageSummary, error)
}

func DetectInspector() Inspector {
	if _, err := exec.LookPath("apt-get"); err == nil {
		return NewAptInspector()
	}
	if _, err := exec.LookPath("dnf"); err == nil {
		return NewDnfInspector()
	}
	return &noopInspector{}
}

type noopInspector struct{}

func (n *noopInspector) Name() string {
	return "none"
}

func (n *noopInspector) Inspect(ctx context.Context) (*PackageSummary, error) {
	return &PackageSummary{
		PackageManager:  "none",
		UpgradableCount: 0,
		SecurityCount:   0,
	}, nil
}
