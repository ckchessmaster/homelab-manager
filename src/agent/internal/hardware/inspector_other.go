//go:build !linux && !windows

package hardware

import (
	"context"
	"time"
)

type otherInspector struct{}

func newPlatformInspector() Inspector {
	return &otherInspector{}
}

func (oi *otherInspector) Inspect(ctx context.Context) (*HardwareSummary, error) {
	return &HardwareSummary{
		Disks:       make([]PhysicalDisk, 0),
		CollectedAt: time.Now().UTC().Format(time.RFC3339),
	}, nil
}
