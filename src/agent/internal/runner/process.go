package runner

import (
	"bufio"
	"context"
	"fmt"
	"io"
	"os/exec"
	"sync"
	"sync/atomic"
	"time"
)

type ProcessRunner struct{}

func NewProcessRunner() *ProcessRunner {
	return &ProcessRunner{}
}

func (r *ProcessRunner) ExecuteCommand(
	ctx context.Context,
	jobID string,
	command string,
	args []string,
	onFrame func(Frame),
) error {
	var seq int64

	emitFrame := func(streamType, line string) {
		currentSeq := atomic.AddInt64(&seq, 1)
		onFrame(Frame{
			JobID:      jobID,
			SequenceID: currentSeq,
			StreamType: streamType,
			LogLine:    line,
			Timestamp:  time.Now().UTC(),
		})
	}

	emitFrame("system", fmt.Sprintf("Starting execution: %s %v", command, args))

	cmd := exec.CommandContext(ctx, command, args...)

	stdoutPipe, err := cmd.StdoutPipe()
	if err != nil {
		emitFrame("system", fmt.Sprintf("Failed to acquire stdout pipe: %v", err))
		return err
	}

	stderrPipe, err := cmd.StderrPipe()
	if err != nil {
		emitFrame("system", fmt.Sprintf("Failed to acquire stderr pipe: %v", err))
		return err
	}

	if err := cmd.Start(); err != nil {
		emitFrame("system", fmt.Sprintf("Failed to start process: %v", err))
		return err
	}

	var wg sync.WaitGroup
	wg.Add(2)

	// Stream stdout
	go func() {
		defer wg.Done()
		streamLines(stdoutPipe, func(line string) {
			emitFrame("stdout", line)
		})
	}()

	// Stream stderr
	go func() {
		defer wg.Done()
		streamLines(stderrPipe, func(line string) {
			emitFrame("stderr", line)
		})
	}()

	// Wait for pipe readers to finish
	wg.Wait()

	// Wait for process termination
	waitErr := cmd.Wait()
	if waitErr != nil {
		if exitErr, ok := waitErr.(*exec.ExitError); ok {
			emitFrame("system", fmt.Sprintf("Process exited with code %d", exitErr.ExitCode()))
			return waitErr
		}
		emitFrame("system", fmt.Sprintf("Process error: %v", waitErr))
		return waitErr
	}

	emitFrame("system", "Process completed successfully (exit code 0)")
	return nil
}

func streamLines(r io.Reader, onLine func(string)) {
	scanner := bufio.NewScanner(r)
	for scanner.Scan() {
		onLine(scanner.Text())
	}
}
