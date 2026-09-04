# Plan 06: Compute Node Agent Daemon (Go)

**Target Milestone:** Milestone 2 (Agent Architecture & Real-Time Console)  
**Status:** ✅ Completed  
**Dependencies:** [Plan 02: Data Layer](file:///home/ckingdon/projects/homelab-manager/docs/plans/02-data-layer-and-storage.md)  

---

## 1. Objectives & Overview

Build the lightweight, static compute node agent daemon in **Go**:
1. Single statically linked executable (`CGO_ENABLED=0`) targeting `linux/amd64` and `linux/arm64` (<15MB binary size, <10MB idle RSS).
2. Gather host metrics: CPU utilization, memory usage percentage, root filesystem headroom.
3. Detect pending reboot states (`/var/run/reboot-required` on Debian/Ubuntu; `needs-restarting -r` on RHEL).
4. Inspect pending OS package upgrades via native package manager checks (`apt list --upgradable` or `dnf check-update`).
5. Support command execution envelopes: spawn processes, capture raw `stdout` and `stderr` streams, and frame output into monotonic sequential chunks.
6. Provide a production-ready `systemd` service unit file template.

---

## 2. Target File Structure

```
src/agent/
├── go.mod
├── go.sum
├── cmd/
│   └── agent/
│       └── main.go
├── internal/
│   ├── config/
│   │   └── config.go
│   ├── metrics/
│   │   ├── sys_linux.go
│   │   └── sys_windows.go
│   ├── packages/
│   │   ├── apt.go
│   │   ├── dnf.go
│   │   └── inspector.go
│   └── runner/
│       ├── process.go
│       └── frame.go
└── packaging/
    └── systemd/
        └── controlplane-agent.service
```

---

## 3. Implementation Details

### Step 1: Configuration & CLI Flags
* Agent arguments:
  * `--hub-url` (e.g. `wss://controlplane.homelab.local/agent-hub`)
  * `--token` (Node registration / authentication token)
  * `--node-id` (Persistent UUID generated or read from `/etc/controlplane/node-id`)
  * `--heartbeat-interval` (Default: 10s)

### Step 2: Metric & System Inspection
* **Metrics Collector:**
  * CPU: calculate sampling from `/proc/stat` or lightweight system info.
  * Memory: read `/proc/meminfo` (MemTotal, MemAvailable).
  * Disk: `syscall.Statfs` on `/` to calculate free percentage.
* **Pending Reboot Detection:**
  * Debian/Ubuntu: check existence of `/var/run/reboot-required` or `/run/reboot-required`.
  * RHEL: execute `needs-restarting -r` exit code check.
* **Package Summary:**
  * Parse upgradable package count and identify security packages from `apt-get -s upgrade` / `apt list --upgradable`.

### Step 3: Process Execution Envelope
* Runner function: `ExecuteCommand(ctx, jobID, command, args, onFrameCallback)`
* Capture process `stdout` and `stderr` using piped readers.
* Emit frames: `Frame{JobID, SequenceID, StreamType ("stdout"|"stderr"), Content, Timestamp}`.
* Handle process exit code and signal propagation.

### Step 4: Systemd Service Unit Template
```ini
[Unit]
Description=ControlPlane Compute Node Agent
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
ExecStart=/usr/local/bin/controlplane-agent --hub-url {{HUB_URL}} --token {{NODE_TOKEN}}
Restart=always
RestartSec=5
KillMode=process
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
```

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Build static binaries for x86_64 and arm64
cd src/agent
GOOS=linux GOARCH=amd64 CGO_ENABLED=0 go build -ldflags="-s -w" -o dist/controlplane-agent-linux-amd64 ./cmd/agent
GOOS=linux GOARCH=arm64 CGO_ENABLED=0 go build -ldflags="-s -w" -o dist/controlplane-agent-linux-arm64 ./cmd/agent

# Verify binary sizes are well below 15MB
ls -lh dist/
```

### Acceptance Criteria
- [x] Statically linked binaries compile for both `amd64` and `arm64` without external libc dependencies.
- [x] Binary size is < 15MB and idle memory usage is < 10MB RSS.
- [x] Running in test mode accurately detects CPU, memory, disk free percentage, and pending reboot status on Linux.
- [x] Command runner captures and frames `stdout`/`stderr` with strictly monotonic sequence numbers.
