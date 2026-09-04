package runner

import "time"

type Frame struct {
	JobID      string    `json:"jobId"`
	SequenceID int64     `json:"sequenceId"`
	StreamType string    `json:"streamType"` // "stdout", "stderr", "system"
	LogLine    string    `json:"logLine"`
	Timestamp  time.Time `json:"timestamp"`
}
