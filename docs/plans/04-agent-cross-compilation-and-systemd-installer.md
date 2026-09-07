# Plan 04: Agent Cross-Platform Compilation & Automated Systemd Installer

**Phase:** Phase 4: Productionization & Deployment  
**Status:** ⏳ Not Started  
**Dependencies:** [Phase 1 Agent Daemon](file:///home/ckingdon/projects/homelab-manager/docs/plans/archive/phase1-mvp/06-compute-node-agent.md)  

---

## 1. Objectives & Overview

Package the Go compute node agent daemon (`controlplane-agent`) for seamless fleet-wide deployment across diverse homelab hardware architectures (x86_64, ARM64/Raspberry Pi) and provide an automated systemd bootstrap script:

1. **Multi-Architecture Static Compilation**:
   * Build statically linked, zero-dependency binaries using `CGO_ENABLED=0` and stripped symbols (`-ldflags="-s -w"`).
   * Target architectures:
     * `linux/amd64` (Intel/AMD servers, Proxmox VMs)
     * `linux/arm64` (Raspberry Pi 4/5, ARM SBCs, Ampere instances)
     * `windows/amd64` (Windows Server / desktop nodes)

2. **Automated Linux Installer Script (`scripts/install-agent.sh`)**:
   * Auto-detect host architecture (`uname -m` ➔ `amd64` vs `arm64`).
   * Download the appropriate static binary from GitHub Releases or local API server.
   * Install binary to `/usr/local/bin/controlplane-agent` with permissions `0755`.
   * Provision `/etc/controlplane/agent.json` containing server URL, API key / enrollment token, and node tags.
   * Generate `/etc/systemd/system/controlplane-agent.service` with automatic restart on failure and graceful signal handling.
   * Reload systemd daemon, enable on boot, and start the service immediately.

3. **Dynamic API Script Endpoint**:
   * Implement `GET /api/v1/agent/install.sh` on `ControlPlane.Api` serving the installer script pre-populated with the server's public endpoint and an ephemeral enrollment token.
   * Homelab operators can adopt any new Linux node with a single command:
     ```bash
     curl -sSL http://controlplane.homelab.local:5000/api/v1/agent/install.sh | sudo bash
     ```

---

## 2. Target File Structure

```
scripts/
├── build-agent-matrix.sh                    # Cross-compilation matrix script
└── install-agent.sh                         # Generic Linux systemd installer script

src/ControlPlane.Api/
└── Features/
    └── Agent/
        └── AgentInstallScriptEndpoints.cs   # GET /api/v1/agent/install.sh & binary downloads

src/agent/
└── (Go daemon source files)
```

---

## 3. Implementation Steps

1. **Build Cross-Compilation Script (`scripts/build-agent-matrix.sh`)**:
   * Execute `go build` with matrix:
     * `GOOS=linux GOARCH=amd64` ➔ `bin/controlplane-agent-linux-amd64`
     * `GOOS=linux GOARCH=arm64` ➔ `bin/controlplane-agent-linux-arm64`
     * `GOOS=windows GOARCH=amd64` ➔ `bin/controlplane-agent-windows-amd64.exe`
   * Calculate SHA256 checksums.

2. **Develop Linux Systemd Installer (`scripts/install-agent.sh`)**:
   * Validate root/sudo privileges.
   * Detect architecture, download binary, create config file, create systemd unit.
   * Output clean status logs with ANSI colors.

3. **API Serving Endpoint**:
   * Add endpoint `GET /api/v1/agent/install.sh` returning dynamically rendered shell script with server URL.
   * Add endpoint `GET /api/v1/agent/binaries/{arch}` streaming pre-compiled binary.

4. **Verify**:
   * Run cross-compilation on both AMD64 and ARM64.
   * Test installer execution on a Linux host/container and inspect `systemctl status controlplane-agent`.

---

## 4. Acceptance Criteria

- [ ] Multi-architecture compilation script generates static binaries for `linux/amd64`, `linux/arm64`, and `windows/amd64`.
- [ ] Binaries are under 15MB and have zero external shared library dependencies.
- [ ] Installer script auto-detects architecture, writes config, configures systemd, and starts the daemon cleanly.
- [ ] `GET /api/v1/agent/install.sh` provides a turnkey 1-liner install experience.
