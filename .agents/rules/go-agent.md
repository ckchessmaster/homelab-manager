# Go Compute Node Agent Rules

Guidelines for writing the lightweight, static `controlplane-agent` daemon in Go.

## 1. Binary Architecture & Constraints
* **Language:** Go 1.22+.
* **Static Linking:** Must compile with `CGO_ENABLED=0` to eliminate runtime libc dependencies:
  ```bash
  GOOS=linux GOARCH=amd64 CGO_ENABLED=0 go build -ldflags="-s -w" -o dist/controlplane-agent-linux-amd64 ./cmd/agent
  GOOS=linux GOARCH=arm64 CGO_ENABLED=0 go build -ldflags="-s -w" -o dist/controlplane-agent-linux-arm64 ./cmd/agent
  ```
* **Size & Footprint:**
  * Executable binary size must remain under **15 MB**.
  * Idle resident memory (RSS) must remain under **10 MB**.
* **Zero Inbound Ports:** The agent initiates an outbound WebSocket (`wss://`) connection to the backend hub. It must **never** listen on an open TCP port.

## 2. Agent Responsibilities & Loops
1. **Heartbeat Loop (every 10s):**
   * Collect CPU utilization, memory percentage, disk headroom.
   * Query upgradable packages (`apt list --upgradable` or `dnf check-update`).
   * Detect pending reboot (`/var/run/reboot-required` on Debian/Ubuntu, `needs-restarting -r` on RHEL).
   * Emit JSON heartbeat payload to backend WebSocket.
2. **Command Execution Envelope:**
   * Receive upgrade and inspection tasks from backend.
   * Spawn target process, intercepting `stdout` and `stderr` streams.
   * Split output into lines or chunks, tag with a monotonic sequence ID, and send immediate WebSocket frames.
3. **Reboot Protocol:**
   * When instructed to reboot, flush all pending log buffers.
   * Send `REBOOT_COMMENCING` message to the hub.
   * Cleanly trigger OS reboot (`systemctl reboot` or `shutdown -r now`) and terminate process.

## 3. Deployment & Service Wrapper
* Linux: Systemd unit file installed at `/etc/systemd/system/controlplane-agent.service` running as `root` (or scoped sudoer for package commands).
* Windows: Runs as a native Windows Service.
