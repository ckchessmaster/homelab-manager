//go:build !windows

package licensing

import (
	"context"
)

type otherInspector struct{}

func newPlatformInspector() Inspector {
	return &otherInspector{}
}

func (oi *otherInspector) Inspect(ctx context.Context) (*WindowsLicenseSummary, error) {
	return nil, nil
}
