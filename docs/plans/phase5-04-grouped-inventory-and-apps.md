# Plan 04: Modern Grouped Inventory & Application Workloads Hub

This plan overhauls the **Host Inventory** presentation and creates a dedicated **Applications & Workloads Hub**. Instead of clunky, awkward directory trees, it introduces modern segmented facet chips, collapsible platform groupings (Proxmox clusters, Kubernetes clusters, Baremetal/OOB), and a dedicated workspace for managing containerized applications across clusters.

---

## 🎯 Objectives & Acceptance Criteria

1. **Modern Segmented Filter Bar (Host Inventory):**
   - Clean horizontal facet chip bar at the top of the inventory:
     - Platform facets: `[ All Hosts (N) ]` `[ Proxmox PVE (N) ]` `[ Kubernetes Nodes (N) ]` `[ Baremetal / Physical (N) ]`
     - Health facets: `[ ⚠️ Reboot Required (N) ]` `[ 📦 Updates Available (N) ]` `[ 🟢 Healthy (N) ]`
   - Real-time search by hostname, IP address, OS family, or node tags.

2. **Grouped View vs. Flat List Toggle:**
   - Add view mode toggle: **`Flat List`** vs. **`Grouped by Platform`**.
   - In "Grouped by Platform" mode:
     - **Proxmox Clusters:** Visual group card showing cluster name, total VMs/LXCs, CPU/RAM utilization summary, and nested host rows with clean glassmorphic styling.
     - **Kubernetes Clusters:** Visual group card showing cluster version, node roles (control-plane / worker), and node ready conditions.
     - **Baremetal / Physical Servers:** Group showing BMC/iDRAC IP, OS family, and power status.
   - Smooth expand/collapse state with persistent user preference in `localStorage`.

3. **Dedicated "Applications & Workloads" Workspace:**
   - Add new sidebar navigation item: **"Applications & Workloads"** (or **"Workloads"**).
   - Dedicated dashboard displaying workloads across all connected Kubernetes clusters:
     - Multi-cluster and namespace filter selector.
     - Workload cards: Name, namespace, cluster, replica health donut (e.g. `3/3 Ready`), container images, and creation age.
     - 1-click administrative actions:
       - **Restart Deployment** (triggers rolling rollout).
       - **Scale Replicas** (modal with slider/stepper to increase or decrease desired replicas).
       - **Pod Drawer** (lists running pods, restart counts, node placements, and phase status).

4. **Backend Workload BFF Aggregation:**
   - Add `GET /api/v1/workloads`: High-performance aggregated BFF endpoint pulling workload summaries across all configured Kubernetes clusters.
   - Add `POST /api/v1/workloads/{clusterId}/{namespace}/{name}/restart`: Rollout restart proxy.
   - Add `POST /api/v1/workloads/{clusterId}/{namespace}/{name}/scale`: Replica scaling proxy.

5. **Visual Polish & Ergonomics:**
   - Adhere strictly to modern aesthetics: dark mode theme, subtle glowing status pings, rounded badges, zero placeholder data, and clean micro-animations.

---

## 📁 Target File Structure

```
src/ControlPlane.Api/
└── Features/
    └── Workloads/
        ├── WorkloadModels.cs
        ├── WorkloadService.cs
        └── WorkloadEndpoints.cs

src/frontend/src/
├── api/
│   └── workloads.ts
├── features/
│   ├── hosts/
│   │   ├── HostFilterPills.tsx
│   │   ├── GroupedHostView.tsx
│   │   └── HostsPage.tsx (updated)
│   └── workloads/
│       ├── WorkloadsPage.tsx
│       ├── WorkloadCard.tsx
│       ├── ScaleWorkloadModal.tsx
│       ├── PodDetailDrawer.tsx
│       └── useWorkloads.ts
├── components/layout/
│   ├── AppSidebar.tsx (updated)
│   └── AppHeader.tsx (updated)
└── App.tsx (updated)
```

---

## 📋 Task Checklist

- [x] Implement backend `WorkloadModels.cs`, `WorkloadService.cs`, and `WorkloadEndpoints.cs`.
- [x] Implement `HostFilterPills.tsx` with segmented counts for platforms and health states.
- [x] Implement `GroupedHostView.tsx` with Proxmox, Kubernetes, and Baremetal cluster headers.
- [x] Update `HostsPage.tsx` with view mode toggle (`Flat` vs. `Grouped`) and integrate filter pills.
- [x] Create frontend workload API client in `src/frontend/src/api/workloads.ts` and React Query hooks.
- [x] Build `WorkloadsPage.tsx`, `WorkloadCard.tsx`, `ScaleWorkloadModal.tsx`, and `PodDetailDrawer.tsx`.
- [x] Update `AppSidebar.tsx` and `AppHeader.tsx` to include the **Workloads** view.
- [x] Update Playwright E2E tests covering filter chips, grouped view toggling, and workload scaling.
- [x] Verify `dotnet test` and `npm run build`.
