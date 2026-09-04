package metrics

type Metrics struct {
	CPUUsagePct    float64 `json:"cpuUsagePct"`
	MemoryUsagePct float64 `json:"memoryUsagePct"`
	DiskFreePct    float64 `json:"diskFreePct"`
}

type Collector interface {
	Collect() (*Metrics, error)
	IsRebootRequired() bool
	KernelVersion() string
}

func NewCollector() Collector {
	return newDefaultCollector()
}
