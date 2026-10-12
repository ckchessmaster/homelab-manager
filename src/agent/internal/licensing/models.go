package licensing

type WindowsLicenseSummary struct {
	IsEvaluation       bool   `json:"isEvaluation"`
	Edition            string `json:"edition"`
	Description        string `json:"description,omitempty"`
	LicenseStatus      string `json:"licenseStatus"` // "Licensed", "OOBGrace", "OOTGrace", "NonGenuineGrace", "Notification", "ExtendedGrace", "Unlicensed", "Unknown"
	GracePeriodMinutes int64  `json:"gracePeriodMinutes"`
	DaysRemaining      int    `json:"daysRemaining"`
	HoursRemaining     int    `json:"hoursRemaining"`
	ExpirationDate     string `json:"expirationDate,omitempty"`
	RearmsRemaining    int    `json:"rearmsRemaining"`
	PartialProductKey  string `json:"partialProductKey,omitempty"`
	IsExpiringSoon     bool   `json:"isExpiringSoon"` // <= 14 days remaining
	IsCritical         bool   `json:"isCritical"`     // <= 3 days remaining or rearmsRemaining == 0
	CollectedAt        string `json:"collectedAt"`
	Error              string `json:"error,omitempty"`
}
