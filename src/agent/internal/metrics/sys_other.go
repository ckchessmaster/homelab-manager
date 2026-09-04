//go:build !linux

package metrics

type genericCollector struct{}

func newDefaultCollector() Collector {
	return newGenericCollector()
}

func newGenericCollector() Collector {
	return &genericCollector{}
}

func (c *genericCollector) Collect() (*Metrics, error) {
	return &Metrics{
		CPUUsagePct:    0.0,
		MemoryUsagePct: 0.0,
		DiskFreePct:    50.0,
	}, nil
}

func (c *genericCollector) IsRebootRequired() bool {
	return false
}

func (c *genericCollector) KernelVersion() string {
	return "non-linux"
}
