# React 19 & Frontend Rules

Standards for writing client-side web code for the ControlPlane Single-Page Application (SPA).

## 1. Technology Choices
* **Core:** React 19, TypeScript, Vite.
* **Styling:** Tailwind CSS with modern design tokens (neutral dark modes, glassmorphism, accent badges).
* **Component Library:** shadcn/ui patterns (Radix UI primitives wrapped in Tailwind).
* **Icons:** Lucide React (`lucide-react`).
* **Server State:** TanStack Query (`@tanstack/react-query`) for API fetching, caching, polling, and mutations.
* **Terminal Streaming:** `@xterm/xterm` with `@xterm/addon-fit` for ANSI terminal log streaming.
* **Real-Time Client:** `@microsoft/signalr` for WebSocket hub subscriptions.

## 2. Design Aesthetics & Visual Excellence
* **Professional & Modern:** Design for a sleek, mission-critical operations dashboard. Avoid raw, generic styles. Use subtle border gradients, backdrop blur (`backdrop-blur-md`), and refined dark mode palettes.
* **Status Badges:** Use distinct, high-contrast semantic badges for node states:
  * Healthy / Online: Emerald / Green
  * Reboot Pending: Amber / Yellow
  * Critical / Failed: Rose / Red
  * Updating / In-Flight: Cyan / Blue with subtle pulse animation
* **Dynamic Feedback:** Add micro-interactions (hover states, smooth transition duration, skeleton loaders for table rows).
* **No Placeholders:** All UI dialogs, modals, and buttons must be functional and connected to TanStack Query mutations or realistic mock responses.

## 3. Architecture & Code Structure
* Path aliases: `@/` mapped to `src/`.
* `src/components/`: Reusable primitives (`Button`, `Table`, `Badge`, `Modal`, `Terminal`).
* `src/features/`: Feature-scoped components and hooks:
  * `hosts/`: Host inventory table, adoption modal, host details drawer.
  * `jobs/`: DAG execution visualizer, real-time xterm.js log stream, step progress.
  * `settings/`: Proxmox credentials, API keys, standby sync status.
* `src/api/`: Typed API client methods and TanStack Query query/mutation hooks.
* Environment variables: Use `import.meta.env.VITE_*` prefixes. Respect `VITE_AUTH_MODE=bypass` in development.
