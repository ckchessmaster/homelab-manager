package hardware

type PhysicalDisk struct {
	DeviceID          string            `json:"deviceId"`
	Name              string            `json:"name,omitempty"`
	Model             string            `json:"model,omitempty"`
	SerialNumber      string            `json:"serialNumber,omitempty"`
	MediaType         string            `json:"mediaType"`
	SizeBytes         int64             `json:"sizeBytes"`
	Status            string            `json:"status"` // "Ok", "Warning", "Critical", "Unknown"
	WearOutPercentage *float64          `json:"wearOutPercentage,omitempty"`
	TemperatureC      *float64          `json:"temperatureCelsius,omitempty"`
	SmartHealthStatus string            `json:"smartHealthStatus,omitempty"`
	Attributes        map[string]string `json:"attributes,omitempty"`
}

type HardwareSummary struct {
	Disks        []PhysicalDisk `json:"disks"`
	CollectedAt  string         `json:"collectedAt"`
	ScanDuration string         `json:"scanDuration,omitempty"`
}
