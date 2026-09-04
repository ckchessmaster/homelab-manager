package lifecycle

import (
	"errors"
	"os"
	"path/filepath"
	"testing"
)

func TestReplaceExecutable(t *testing.T) {
	tempDir := t.TempDir()

	srcPath := filepath.Join(tempDir, "agent.tmp")
	dstPath := filepath.Join(tempDir, "agent")

	if err := os.WriteFile(srcPath, []byte("new-version-binary"), 0755); err != nil {
		t.Fatalf("failed to create src file: %v", err)
	}

	if err := os.WriteFile(dstPath, []byte("old-version-binary"), 0755); err != nil {
		t.Fatalf("failed to create dst file: %v", err)
	}

	if err := replaceExecutable(srcPath, dstPath); err != nil {
		t.Fatalf("replaceExecutable failed: %v", err)
	}

	content, err := os.ReadFile(dstPath)
	if err != nil {
		t.Fatalf("failed to read dst: %v", err)
	}

	if string(content) != "new-version-binary" {
		t.Fatalf("expected 'new-version-binary', got '%s'", string(content))
	}
}

func TestFailUpdate(t *testing.T) {
	var capturedMsg interface{}
	writeJSON := func(v interface{}) error {
		capturedMsg = v
		return nil
	}

	testErr := errors.New("binary verification failed")
	err := failUpdate(writeJSON, "node-1", "job-1", "1.1.0", testErr)

	if !errors.Is(err, testErr) {
		t.Fatalf("expected %v, got %v", testErr, err)
	}

	appliedMsg, ok := capturedMsg.(UpdateAppliedMessage)
	if !ok {
		t.Fatalf("expected UpdateAppliedMessage, got %T", capturedMsg)
	}

	if appliedMsg.Success {
		t.Fatalf("expected Success=false, got true")
	}

	if appliedMsg.Error != "binary verification failed" {
		t.Fatalf("expected error string 'binary verification failed', got '%s'", appliedMsg.Error)
	}
}
