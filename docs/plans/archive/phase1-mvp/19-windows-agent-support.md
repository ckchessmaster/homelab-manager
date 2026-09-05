# Plan 19: Windows Compute Node Agent Support & Package Manager Integration

**Target Milestone:** Milestone 5 (Production Hardening, Lifecycle Automation & Distribution)  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 06: Compute Node Agent](file:///home/ckingdon/projects/homelab-manager/docs/plans/06-compute-node-agent.md), [Plan 12: Deterministic Reboot Handler](file:///home/ckingdon/projects/homelab-manager/docs/plans/12-deterministic-reboot-handler.md)

---

## 1. Objectives & Overview

Extend the Go compute node agent daemon (`controlplane-agent`) to support Windows Server and Windows 10/11 Pro/Enterprise compute instances:
1. Implement Windows package manager inspection and update execution using `powershell.exe` with `PSWindowsUpdate` / Windows Update Agent (WUA) API.
2. Detect pending reboot indicators via Windows registry keys (`RebootPending`, `RebootRequired`).
3. Support running the Go agent as a native **Windows Service** via `golang.org/x/sys/windows/svc`.
4. Provide a PowerShell bootstrap script for one-click Windows agent adoption.

---

## 2. Target File Structure

```
src/agent/
├── internal/
│   ├── packages/
│   │   ├── windows.go
│   │   └── windows_test.go
│   └── service/
│       ├── service_windows.go
│       └── service_linux.go
└── scripts/
    └── install-agent.ps1
```

---

## 3. Implementation Details

### Step 1: Windows Update Inspection (`internal/packages/windows.go`)
* Execute PowerShell script to enumerate missing KB updates:
  ```powershell
  Get-CimInstance -ClassName Win32_QuickFixEngineering
  # or invoke Microsoft.Update.Session COM object
  ```
* Parse count of upgradable and security updates.
* Execute updates when receiving `CMD_UPGRADE`.

### Step 2: Windows Reboot Flag Detection
* Check registry keys:
  * `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending`
  * `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired`

### Step 3: Windows Service Lifecycle
* Implement `svc.Handler` for Windows.
* Handle service control requests (`Stop`, `Shutdown`, `Interrogate`).

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Cross-compile agent for Windows
GOOS=windows GOARCH=amd64 go build -o bin/controlplane-agent.exe ./cmd/agent
```

### Acceptance Criteria
- [ ] Agent compiles cleanly for `GOOS=windows GOARCH=amd64`.
- [ ] Windows agent correctly inspects pending updates and registry reboot flags.
- [ ] Agent runs as a Windows Service with clean lifecycle signaling and shutdown.
