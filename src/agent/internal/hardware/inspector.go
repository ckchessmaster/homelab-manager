package hardware

import (
	"context"
)

type Inspector interface {
	Inspect(ctx context.Context) (*HardwareSummary, error)
}

func DetectInspector() Inspector {
	return newPlatformInspector()
}
