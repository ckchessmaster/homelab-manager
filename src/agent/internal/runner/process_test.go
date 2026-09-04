package runner

import (
	"context"
	"testing"
	"time"
)

func TestProcessRunner_ExecuteCommand(t *testing.T) {
	runner := NewProcessRunner()

	var frames []Frame
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	err := runner.ExecuteCommand(ctx, "job-123", "echo", []string{"hello", "world"}, func(f Frame) {
		frames = append(frames, f)
	})

	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	if len(frames) < 3 {
		t.Fatalf("expected at least 3 frames (system start, stdout, system complete), got %d", len(frames))
	}

	// Verify monotonic sequence IDs
	for i := 1; i < len(frames); i++ {
		if frames[i].SequenceID <= frames[i-1].SequenceID {
			t.Errorf("expected sequenceId to be strictly monotonic: frame[%d]=%d, frame[%d]=%d",
				i-1, frames[i-1].SequenceID, i, frames[i].SequenceID)
		}
	}

	// Check stdout line
	foundStdout := false
	for _, f := range frames {
		if f.StreamType == "stdout" && f.LogLine == "hello world" {
			foundStdout = true
			break
		}
	}

	if !foundStdout {
		t.Errorf("did not find expected stdout frame 'hello world'")
	}
}
