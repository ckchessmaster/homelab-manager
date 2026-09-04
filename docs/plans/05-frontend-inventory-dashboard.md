# Plan 05: Frontend Inventory Dashboard (React 19 & Tailwind)

**Target Milestone:** Milestone 1 (Foundation, Data Layer & Host Inventory)  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 04: Host Inventory API](file:///home/ckingdon/projects/homelab-manager/docs/plans/04-host-inventory-api.md)  

---

## 1. Objectives & Overview

Build the initial interactive web UI for the single-pane-of-glass operator dashboard:
1. Construct the application shell: responsive dark-mode sidebar, header with system status indicators, and main viewport.
2. Implement the **Host Inventory Table** showing real-time host states, OS tags, pending reboot indicators, and upgradable package counters.
3. Build the **"Add Host" modal dialog** with form validation to register compute hosts and associate hypervisor/BMC correlation IDs.
4. Integrate TanStack Query for background polling, optimistic updates, and cache invalidation.
5. Provide automatic dev authentication bypass handling when `VITE_AUTH_MODE=bypass`.

---

## 2. Target File Structure

```
src/frontend/src/
├── api/
│   ├── client.ts
│   └── hosts.ts
├── components/
│   ├── layout/
│   │   ├── AppHeader.tsx
│   │   ├── AppSidebar.tsx
│   │   └── Layout.tsx
│   └── ui/
│       ├── badge.tsx
│       ├── button.tsx
│       ├── dialog.tsx
│       ├── input.tsx
│       ├── select.tsx
│       └── table.tsx
└── features/
    └── hosts/
        ├── HostTable.tsx
        ├── HostStatusBadge.tsx
        ├── AddHostModal.tsx
        └── useHosts.ts
```

---

## 3. Implementation Details

### Step 1: Design Tokens & Layout
* Modern dark-themed palette: slate/zinc base (`bg-zinc-950`), subtle borders (`border-zinc-800`), glassmorphic panels (`backdrop-blur-md bg-zinc-900/60`).
* Responsive sidebar with navigation items:
  * 🖥️ **Hosts Inventory** (Active)
  * ⚡ **Workflows & DAGs** (Milestone 3)
  * ⚙️ **Adapters & Settings** (Milestone 4)

### Step 2: Host Table View
* Columns:
  * **Host:** Hostname, friendly name, target type icon (Bare-Metal server, VM, LXC).
  * **IP & Network:** IP address with copy button, switch port if known.
  * **OS & Platform:** Debian, Ubuntu, RHEL, Windows badge.
  * **Agent Status:** Active (green dot), Offline (gray dot), Not Installed (dashed badge).
  * **Vitals:** Upgradable package badge (amber if > 0), Reboot Required badge (pulsing red/amber if true).
  * **Actions:** Quick view details, trigger update (disabled until Milestone 3), delete.
* Filter bar: Search by hostname/IP, filter by OS family, filter by "Reboot Pending" or "Updates Available".

### Step 3: Add Host Modal Dialog
* Form fields:
  * Hostname (string, required)
  * Friendly Name (optional)
  * IP Address (required)
  * OS Family dropdown (`Debian / Ubuntu`, `RHEL / Rocky Linux`, `Windows Server`)
  * Target Type dropdown (`Bare-Metal Server`, `Proxmox VM`, `Proxmox Container`)
  * Collapsible Accordion: *Hypervisor & BMC Linking* (Proxmox Node, VMID, iDRAC IP, UniFi MAC).
* Submission calls `POST /api/v1/hosts` via TanStack Query mutation with toast notification on success.

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Run frontend development server
cd src/frontend && npm run dev
```

### Acceptance Criteria
- [ ] Dashboard displays the host table populated with records from the backend API.
- [ ] Clicking "Add Host" opens the dialog, validates inputs, and inserts the host into the table without full page reload.
- [ ] Hosts with `pendingReboot: true` visually render a distinct amber/red warning badge.
- [ ] Responsive layout adapts smoothly between desktop monitor and tablet/mobile viewports.
- [ ] Milestone 1 deliverables are fully achieved upon completion of this plan.
