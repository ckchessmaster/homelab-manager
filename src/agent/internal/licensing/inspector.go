package licensing

import (
	"context"
)

type Inspector interface {
	Inspect(ctx context.Context) (*WindowsLicenseSummary, error)
}

func DetectInspector() Inspector {
	return newPlatformInspector()
}
