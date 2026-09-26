# ControlPlane UI/UX Design System & Architectural Rules

## 1. Color Palette & Surface Elevation
- **Canvas Base:** `zinc-950` (`#09090b`). Never use pure pitch black (`#000000`).
- **Primary Surfaces & Cards:** `zinc-900` (`#18181b`).
- **Sub-surfaces & Inset Inputs:** `zinc-950/60` with `zinc-800` borders (`#27272a`).
- **Borders & Dividers:** `zinc-800` (`#27272a`). Active/focus rings: `sky-500` / `sky-600`.
- **Hover States:** `zinc-800/60`.
- **Brand Accent:** Single primary accent for CTAs, active tab pills, and focus states: Sky (`sky-500` / `sky-600`). Do not mix random purples, teals, and blues.
- **Semantic Status Signals:** Strictly reserved for health/state:
  - Success / Healthy / Online: `emerald-500`
  - Warning / Pending / Degraded: `amber-500`
  - Critical / Failed / Error: `rose-500`
  - Subdued / Inactive / Unconfigured: `zinc-500`
- **Icon Sizing Standards:**
  - Table and inline action icons: `w-4 h-4` (or `w-3.5 h-3.5` inside compact buttons).
  - Main navigation and section header icons: `w-5 h-5` (18–20px).

## 2. Component Patterns & Layout Scaffolding
- **Right-Hand Inspector Drawer (`<InspectorSheet />`):**
  - Host details, node details, cluster details, and workload configurations must open in a right-sliding slide-over drawer (`w-[560px]`, `slide-in-from-right`).
  - Never use centered floating modals for deep telemetry inspection.
  - Correlation targets must render as a clean horizontal pipeline:
    `[Proxmox: VM <id>] ➔ [K8s: Node] ➔ [UniFi: Port] ➔ [BMC: Info]`
    Unmapped/inactive targets must render as subtle dashed chips (`text-zinc-500 border-dashed border-zinc-800`).
  - Must include a sticky bottom footer for primary mutations (`Run DAG Update`, `Reboot Node`, `Edit Host`).
- **Compact Top Metric Strip (`<MetricStrip />`):**
  - Summary stats must occupy a sleek, single horizontal bar (`h-11`, ~44px height) directly under the page title or toolbar.
  - Never build stacked 4-card hero blocks that push data below the fold.
- **Unified Filter Toolbar (`<TableToolbar />`):**
  - Toolbars across all views must share identical heights (`h-9`), border styling (`zinc-800`), search inputs, and button radiuses (`rounded-md`).
- **Confirmation Guards:**
  - Destructive actions (`Drain Node`, `Reboot Node`, `Delete Workload`) must include confirmation guards before submitting mutations.
- **Full-Viewport DAG View:**
  - The workflow DAG runner must use an immersive full-canvas view (`inset-0`) with a collapsible bottom-docked terminal pane (`h-64`) for SignalR logs.

## 3. Data Density & Tabular Hierarchy
- **Dense DataTables over Cards:**
  - Workloads, TLS certificates, and Host inventory must be rendered in structured, virtualized DataTables.
  - Card grids are strictly forbidden for lists exceeding 6 items on desktop screens.
  - Raw command strings, long image tags, and hashes must be truncated into mono chips with click-to-copy; never dump full multiline strings directly into table cells.

## 4. Mobile & Touch Ergonomics Invariants (< 768px)
- **Responsive Layout Shell (`<MobileBottomNav />`):**
  - On viewports < 768px, hide the fixed desktop sidebar (`hidden md:flex`) and render a sleek bottom navigation bar fixed to the viewport with safe-area bottom padding (`env(safe-area-inset-bottom)`).
  - Secondary views (Adapters, Settings) must open in a slide-up "More" drawer.
- **Table / List Card Responsive Split:**
  - The "Dense DataTables over Cards" rule applies to desktop screens (`>= 768px`). On mobile screens (`< 768px`), tabular data must transform into touch-friendly list cards with kebab menus, status dots, and tap-to-inspect gestures.
- **Touch Target Dimensions:**
  - All interactive triggers, buttons, and dropdown kebabs must maintain a minimum touch target area of 44×44px.
- **Mobile DAG Presentation (`<MobileDagTimeline />`):**
  - Complex 2D node graphs must render as a vertical step timeline on small viewports with inline approval gates and expandable log drawers to eliminate gesture conflicts.