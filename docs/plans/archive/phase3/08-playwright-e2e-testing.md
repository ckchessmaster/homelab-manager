# Plan 08: End-to-End Automated UI Testing with Playwright

**Phase:** Phase 3 / Verification  
**Status:** ✅ Completed  
**Dependencies:** [Plan 04: Node-Based Visual Workflow Canvas UI](file:///home/ckingdon/projects/homelab-manager/docs/plans/04-node-based-workflow-canvas-ui.md), [Plan 05: Parameterized Workflow Launcher](file:///home/ckingdon/projects/homelab-manager/docs/plans/05-parameterized-workflow-launcher.md), [Plan 06: Multi-Node Fleet Rolling Orchestrator](file:///home/ckingdon/projects/homelab-manager/docs/plans/06-multi-node-fleet-rolling-orchestration.md)

---

## 1. Objectives & Overview

Establish a comprehensive, resilient **Playwright End-to-End (E2E) UI Test Suite** in `src/frontend` to automatically validate all critical homelab operator workflows:

1. **Playwright Harness & Vite WebServer Integration**:
   * Add `@playwright/test` to `src/frontend/devDependencies`.
   * Configure `playwright.config.ts` targeting `http://localhost:5173` with automated Vite dev server spawning (`webServer: { command: 'npm run dev', port: 5173, reuseExistingServer: true }`).
   * Support headless execution, failure screenshots, video recordings, and trace artifacts.

2. **Deterministic Mock API Harness**:
   * Implement `src/frontend/e2e/fixtures/mockApi.ts` using Playwright's `page.route()` interception.
   * Provide pre-canned, realistic fixtures for:
     * Hosts (`/api/v1/hosts`) with Debian, Ubuntu, Proxmox, and baremetal nodes.
     * Discovery scans (`/api/v1/discovery/scan`) with detected VMs, LXCs, and baremetal candidates.
     * Temporal Workflows (`/api/v1/workflows/preview`, `/api/v1/workflows/launch`, active workflows, signals).
     * System Settings & Adapters (`/api/v1/settings/*`).
   * Enable running all tests offline and instantaneously without requiring live Proxmox, Kubernetes, or PostgreSQL infrastructure.

3. **Core Operator Test Suites**:
   * **`navigation.spec.ts`**: Sidebar navigation across Hosts, Discovery, Workflows, Audit Logs, and Settings views; active route indicator, responsive collapsible sidebar.
   * **`hosts.spec.ts`**: Host table rendering, search query filtering, OS badge styling, reboot required indicator, Add Host dialog validation and submission, Host deletion cascade.
   * **`terminal.spec.ts`**: Opening the sliding Host Terminal Drawer, confirming `@xterm/xterm` canvas mounting, command input execution, auto-scroll toggling, and clean drawer unmounting.
   * **`discovery.spec.ts`**: Candidate resource listing, filter toggling (Proxmox vs Kubernetes vs Baremetal; Managed vs Unmanaged), candidate import modal prefilling and host creation.
   * **`workflows-dag.spec.ts`**: Workflows page, Parameterized Workflow Launcher slide-over modal, live interactive `@xyflow/react` DAG canvas rendering, dynamic step addition/removal (snapshots, cordon/drain, approval gates, health probes), and workflow launch submission.
   * **`fleet-rolling.spec.ts`**: Multi-node fleet rolling upgrade modal, multi-host selection, concurrency batching controls, and rolling status tracker view.

4. **NPM Test Scripts**:
   * Add `"test:e2e": "playwright test"` and `"test:e2e:ui": "playwright test --ui"` to `src/frontend/package.json`.

---

## 2. Target File Structure

```
src/frontend/
├── playwright.config.ts                     # Playwright configuration with Vite dev server integration
├── package.json                             # Added @playwright/test dependency & e2e scripts
└── e2e/
    ├── fixtures/
    │   └── mockApi.ts                       # Intercepted API routes and realistic homelab mock data
    ├── navigation.spec.ts                   # Shell navigation & view switching
    ├── hosts.spec.ts                        # Host inventory, search, add, delete
    ├── terminal.spec.ts                     # Host terminal drawer & xterm.js canvas rendering
    ├── discovery.spec.ts                    # Infrastructure discovery & candidate import
    ├── workflows-dag.spec.ts                # Visual DAG canvas & parameterized workflow launcher
    └── fleet-rolling.spec.ts                # Multi-host fleet rolling upgrade orchestrator modal
```

---

## 3. Implementation Steps

1. **Install Playwright & Browsers**:
   ```bash
   cd src/frontend && npm install -D @playwright/test && npx playwright install chromium
   ```

2. **Create `playwright.config.ts`**:
   * Set `testDir: './e2e'`.
   * Configure viewport (1280x800), color scheme (`dark`), trace, screenshot.
   * Configure `webServer` targeting Vite on port 5173.

3. **Build `mockApi.ts` Fixture Helper**:
   * Intercept `/api/v1/hosts`, `/api/v1/discovery/*`, `/api/v1/workflows/*`, `/api/v1/settings/*`.
   * Support overriding specific endpoint data per test.

4. **Implement Test Specs**:
   * `navigation.spec.ts`
   * `hosts.spec.ts`
   * `terminal.spec.ts`
   * `discovery.spec.ts`
   * `workflows-dag.spec.ts`
   * `fleet-rolling.spec.ts`

5. **Verify**:
   * Run `npm run test:e2e` in `src/frontend` and ensure all tests pass cleanly in headless mode.

---

## 4. Acceptance Criteria

- [x] `@playwright/test` is configured in `src/frontend/package.json` with runnable scripts.
- [x] `playwright.config.ts` automatically boots the Vite dev server during test execution.
- [x] Mock API fixture provides full isolation from live infrastructure.
- [x] All 6 spec files execute and pass without flakiness or timeout failures.
- [x] Terminal canvas and visual DAG ReactFlow canvas mount and render without errors in Chromium.
