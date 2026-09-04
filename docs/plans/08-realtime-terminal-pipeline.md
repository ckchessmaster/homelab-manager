# Plan 08: Real-Time Terminal Streaming Pipeline (SignalR & xterm.js)

**Target Milestone:** Milestone 2 (Agent Architecture & Real-Time Console)  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 05: Frontend Dashboard](file:///home/ckingdon/projects/homelab-manager/docs/plans/05-frontend-inventory-dashboard.md), [Plan 07: WebSocket Agent Hub](file:///home/ckingdon/projects/homelab-manager/docs/plans/07-websocket-agent-hub.md)  

---

## 1. Objectives & Overview

Deliver low-latency, real-time command output visibility superior to Ansible's black-box execution:
1. The compute node agent captures `stdout` and `stderr` streams, frames each chunk with a monotonic `sequence_id`, and sends it over the WebSocket connection.
2. The ASP.NET Core backend processes incoming frames, persists them to the `step_logs` table in EF Core, and broadcasts them via a SignalR hub (`Hub<IJobClient>`).
3. The React frontend mounts an **`xterm.js`** virtual terminal canvas and renders ANSI colors, terminal escape sequences, and status lines in real time.
4. Provide auto-scroll, search, clear, and copy controls in the terminal UI.

---

## 2. Target File Structure

```
src/ControlPlane.Api/
├── Hubs/
│   ├── JobLogHub.cs
│   └── IJobClient.cs
└── Features/
    └── Jobs/
        ├── StepLogStreamConsumer.cs
        └── StepLogQueryEndpoints.cs

src/frontend/src/
├── components/
│   └── terminal/
│       ├── TerminalCanvas.tsx
│       ├── TerminalToolbar.tsx
│       └── useJobTerminalStream.ts
```

---

## 3. Implementation Details

### Step 1: Backend SignalR Hub & Persistence
* Define client contract:
  ```csharp
  public interface IJobClient
  {
      Task ReceiveLogLine(Guid jobId, long sequenceId, string streamType, string logLine, DateTimeOffset timestamp);
      Task JobStatusChanged(Guid jobId, string status, string? activeStep);
  }
  ```
* Map hub route `/hubs/jobs`.
* When the agent sends an output frame over WebSocket:
  * Append to `step_logs` table in batches or buffered channel.
  * Invoke `await hubContext.Clients.Group(jobId.ToString()).ReceiveLogLine(...)`.

### Step 2: Historical Log Replay Endpoint
* `GET /api/v1/jobs/{id}/logs?fromSequenceId=0`:
  * Returns buffered historical log lines so refreshing the browser or opening the drawer mid-execution replays existing output without missing a line.

### Step 3: Frontend xterm.js Terminal Canvas
* Install `@xterm/xterm` and `@xterm/addon-fit`.
* Build `TerminalCanvas.tsx`:
  * Theme matching the dashboard palette (dark background `#09090b`, ANSI green, yellow, red, cyan accents).
  * Auto-fit container with `ResizeObserver`.
  * Auto-scroll toggle (pauses when user scrolls up, resumes when scrolled to bottom).
* Build `useJobTerminalStream(jobId)` hook:
  * Connects to `/hubs/jobs` using `@microsoft/signalr`.
  * Joins group `jobId`.
  * Replays missed logs from `/api/v1/jobs/{id}/logs`.
  * Writes incoming frames into the terminal instance via `term.write(frame.logLine + '\r\n')`.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Execute mock command from backend to agent
curl -X POST http://localhost:5000/api/v1/debug/execute-command \
  -H "Content-Type: application/json" \
  -d '{"hostId": "...", "command": "apt-get", "args": ["update"]}'
```

### Acceptance Criteria
- [ ] Command output is received in real-time on the browser with zero perceptible delay.
- [ ] ANSI escape codes (colors, bold text, progress bars) render cleanly without garbled characters.
- [ ] Terminal auto-scrolls down as new lines arrive, and pauses when the operator scrolls back up.
- [ ] Reloading the page or switching tabs replays previous output up to the current sequence ID.
