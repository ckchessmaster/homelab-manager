package metrics

import "math"

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

func round(val float64, precision int) float64 {
	ratio := math.Pow(10, float64(precision))
	return math.Round(val*ratio) / ratio
}
