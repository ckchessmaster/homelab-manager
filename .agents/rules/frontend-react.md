# React 19 & Frontend Rules

Standards for writing client-side web code for the ControlPlane Single-Page Application (SPA).

> **CRITICAL GOVERNANCE:** When generating or modifying UI components, pages, or styles, strictly adhere to the rules, tokens, and component patterns defined in `/DESIGN_SYSTEM.md`. Never invent arbitrary hex colors, use pitch black (`#000000`) backgrounds, or build card grids for lists exceeding 6 items. Always use `<MetricStrip />` for summary stats, `<TableToolbar />` for filtering, and `<InspectorSheet />` for deep telemetry inspection.

## 1. Technology Choices
* **Core:** React 19, TypeScript, Vite.
* **Styling:** Tailwind CSS with modern design tokens (neutral dark modes, glassmorphism, accent badges).
* **Component Library:** shadcn/ui patterns (Radix UI primitives wrapped in Tailwind).
* **Icons:** Lucide React (`lucide-react`).
* **Authentication:** `react-oidc-context` (Zitadel OIDC with PKCE) and local development bypass.
* **Server State:** TanStack Query (`@tanstack/react-query`) for API fetching, caching, polling, and mutations.
* **Terminal Streaming:** `@xterm/xterm` with `@xterm/addon-fit` for ANSI terminal log streaming.
* **Real-Time Client:** `@microsoft/signalr` for WebSocket hub subscriptions.
* **Testing:** Playwright E2E test suite (`tests/e2e/`).

## 2. Design Aesthetics & Visual Excellence
* **Professional & Modern:** Design for a sleek, mission-critical operations dashboard. Avoid raw, generic styles. Use subtle border gradients, backdrop blur (`backdrop-blur-md`), and refined dark mode palettes.
* **Status Badges:** Use distinct, high-contrast semantic badges for node states:
  * Healthy / Online: Emerald / Green
  * Reboot Pending: Amber / Yellow
  * Critical / Failed: Rose / Red
  * Updating / In-Flight: Cyan / Blue with subtle pulse animation
* **Segmented Filter Chips & Grouping:** Use modern segmented filter chips and badge counts for inventory slicing. Avoid clumsy tree navigations.
* **Dynamic Feedback:** Add micro-interactions (hover states, smooth transition duration, skeleton loaders for table rows).
* **No Placeholders:** All UI dialogs, modals, and buttons must be functional and connected to TanStack Query mutations or realistic mock responses.

## 3. Architecture & Code Structure
* Path aliases: `@/` mapped to `src/`.
* `src/components/`: Reusable primitives (`Button`, `Table`, `Badge`, `Modal`, `Terminal`).
* `src/features/`: Feature-scoped components and hooks:
  * `auth/`: Zitadel OIDC integration, `useAuth()` hook, `<RequireRole role="...">` gate, user profile menu.
  * `hosts/`: Host inventory table, grouped inventory chips, adoption modal, host details drawer.
  * `adapters/`: Unified Adapters Hub (Proxmox, Kubernetes, UniFi, OPNsense, iDRAC/Redfish instances, connection testing, credentials modals).
  * `workloads/`: Multi-cluster Kubernetes applications and workloads view, deployment scaling dialog, rollout restart trigger.
  * `jobs/`: DAG execution visualizer, real-time xterm.js log stream, step progress.
* `src/api/`: Typed API client methods with authenticated request interceptor and TanStack Query query/mutation hooks.
* Environment variables: Use `import.meta.env.VITE_*` prefixes (`VITE_OIDC_AUTHORITY`, `VITE_OIDC_CLIENT_ID`, `VITE_AUTH_MODE=bypass`).
