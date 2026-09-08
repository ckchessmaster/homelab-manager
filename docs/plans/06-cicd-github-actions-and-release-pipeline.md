# Plan 06: CI/CD GitHub Actions Automation & Release Pipeline

**Phase:** Phase 4: Productionization & Deployment  
**Status:** ⏳ Not Started  
**Dependencies:** [Plan 01: Production Containerization](file:///home/ckingdon/projects/homelab-manager/docs/plans/01-production-containerization-and-compose.md), [Plan 05: Helm Chart](file:///home/ckingdon/projects/homelab-manager/docs/plans/05-kubernetes-production-packaging-and-helm.md)  

---

## 1. Objectives & Overview

Establish robust, enterprise-grade Continuous Integration (CI) and Continuous Delivery (CD) automation with **GitHub Actions**:

1. **Continuous Integration (`.github/workflows/ci.yml`)**:
   * Triggered on pull requests and pushes to `main`.
   * **Backend Job**: .NET 10 SDK setup, restore dependencies, compile solution with warnings-as-errors, run unit tests (`tests/ControlPlane.Api.Tests`), report test results and code coverage.
   * **Agent Job**: Go 1.22+ setup, compile agent daemon, run Go tests.
   * **Frontend Job**: Node 22 setup, dependency install (`npm ci`), lint, typecheck (`tsc -b`).
   * **E2E Job**: Run all 14 Playwright test suites in headless Chromium with mock API fixtures.
   * **Helm Job**: Lint Helm chart using `ct lint` / `helm lint`.

2. **Automated Container & Binary Release (`.github/workflows/release.yml`)**:
   * Triggered on Git version tags (`v*.*.*`) or manual dispatch.
   * **Multi-Arch OCI Images**: Build and publish `controlplane-api` and `controlplane-frontend` to **GitHub Container Registry (GHCR)** (`ghcr.io/...`) for `linux/amd64` and `linux/arm64` using Docker Buildx.
   * **Agent Release Binaries**: Cross-compile static agent binaries for `linux/amd64`, `linux/arm64`, and `windows/amd64`, generate SHA256 checksums, and attach to the GitHub Release.
   * **Helm Chart Release**: Package Helm chart and publish to GHCR as an OCI artifact.

3. **Security & Vulnerability Scanning**:
   * Integrate Trivy / GitHub Dependency Review to scan container images and NuGet/NPM dependencies for critical CVEs.

---

## 2. Target File Structure

```
.github/
└── workflows/
    ├── ci.yml                               # PR & commit validation pipeline
    ├── release.yml                          # Container & binary distribution pipeline
    └── security-scan.yml                    # Dependency & container vulnerability audit
```

---

## 3. Implementation Steps

1. **Create `.github/workflows/ci.yml`**:
   * Parallel matrix jobs for .NET API, Go Agent, React Frontend, and Playwright E2E.
   * Cache NuGet packages, Go modules, and NPM node_modules for ultra-fast CI cycles.

2. **Create `.github/workflows/release.yml`**:
   * Configure `docker/setup-buildx-action` and `docker/login-action` against `ghcr.io`.
   * Build multi-arch images (`linux/amd64,linux/arm64`) with semantic tags (`latest`, `v1.0.0`).
   * Compile Go agent matrix, package as tarballs/zip, and attach to release using `softprops/action-gh-release`.

3. **Verify Locally / Workflow Linting**:
   * Verify YAML syntax using GitHub Actions actionlint.
   * Ensure paths, environment variables, and job dependencies are strictly defined.

---

## 4. Acceptance Criteria

- [ ] CI workflow runs backend tests, frontend typecheck, and Playwright E2E tests cleanly.
- [ ] Release workflow builds multi-arch OCI images for both API and Frontend.
- [ ] Release workflow cross-compiles Go agent binaries and generates checksums.
- [ ] Helm chart linting is integrated into the automated PR gate.
