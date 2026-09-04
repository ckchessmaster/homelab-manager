# Plan 01: Solution Scaffolding (.NET 10, Aspire & React 19)

**Target Milestone:** Milestone 1 (Foundation, Data Layer & Host Inventory)  
**Status:** ⏳ Not Started  
**Dependencies:** None  

---

## 1. Objectives & Overview

Scaffold the core multi-project solution structure for ControlPlane:
1. Initialize the root .NET solution (`ControlPlane.sln`).
2. Scaffold `.NET 10` Aspire AppHost (`src/Aspire/ControlPlane.AppHost`) and ServiceDefaults (`src/Aspire/ControlPlane.ServiceDefaults`).
3. Scaffold the ASP.NET Core BFF API (`src/ControlPlane.Api`) with health check endpoints and Aspire service discovery wiring.
4. Scaffold the client-side SPA in `src/frontend` using React 19, TypeScript, Vite, and Tailwind CSS.
5. Wire the frontend into the Aspire AppHost as an executable/npm resource to allow one-command multi-tier debugging.

> [!IMPORTANT]
> Aspire APIs change rapidly. Consult the latest Aspire documentation when referencing NuGet package versions and `DistributedApplicationBuilder` APIs.

---

## 2. Target File Structure

```
homelab-manager/
├── ControlPlane.sln
├── src/
│   ├── Aspire/
│   │   ├── ControlPlane.AppHost/
│   │   │   ├── ControlPlane.AppHost.csproj
│   │   │   ├── Program.cs
│   │   │   └── appsettings.json
│   │   └── ControlPlane.ServiceDefaults/
│   │       ├── ControlPlane.ServiceDefaults.csproj
│   │       └── Extensions.cs
│   ├── ControlPlane.Api/
│   │   ├── ControlPlane.Api.csproj
│   │   ├── Program.cs
│   │   └── appsettings.json
│   └── frontend/
│       ├── package.json
│       ├── tsconfig.json
│       ├── vite.config.ts
│       ├── index.html
│       └── src/
│           ├── App.tsx
│           ├── main.tsx
│           └── index.css
```

---

## 3. Implementation Steps

### Step 1: Solution & Aspire Projects
1. Run `dotnet new sln -n ControlPlane`.
2. Create `src/Aspire/ControlPlane.AppHost` using `dotnet new aspire-apphost` (targeting `net10.0`).
3. Create `src/Aspire/ControlPlane.ServiceDefaults` using `dotnet new aspire-servicedefaults` (targeting `net10.0`).
4. Add projects to `ControlPlane.sln`.

### Step 2: Backend API Project
1. Create `src/ControlPlane.Api` using `dotnet new webapi -minimal -f net10.0`.
2. Add reference to `ControlPlane.ServiceDefaults`.
3. In `Program.cs`, add `builder.AddServiceDefaults();` and `app.MapDefaultEndpoints();`.
4. Add project to `ControlPlane.sln`.

### Step 3: Frontend Project (React 19 + Vite)
1. In `src/frontend`, initialize React 19 + TypeScript template via `npm create vite@latest . -- --template react-ts`.
2. Install and configure Tailwind CSS:
   ```bash
   npm install -D tailwindcss @tailwindcss/vite
   ```
3. Install standard frontend dependencies: `@tanstack/react-query`, `lucide-react`, `clsx`, `tailwind-merge`.
4. Configure Vite proxy in `vite.config.ts` to forward `/api` requests to the backend during local standalone development.

### Step 4: Aspire Orchestration Wiring
1. In `src/Aspire/ControlPlane.AppHost/Program.cs`:
   * Register `api = builder.AddProject<Projects.ControlPlane_Api>("api");`
   * Register frontend: `builder.AddNpmApp("frontend", "../../frontend", "dev").WithReference(api).WithHttpEndpoint(env: "PORT");`

---

## 4. Verification & Acceptance Criteria

### Verification Commands
```bash
# Verify backend solution builds cleanly
dotnet build ControlPlane.sln

# Verify frontend builds without type errors
cd src/frontend && npm install && npm run build
```

### Acceptance Criteria
- [ ] `ControlPlane.sln` compiles with 0 errors and 0 warnings.
- [ ] Aspire AppHost launches and displays dashboard with `api` and `frontend` services visible.
- [ ] Requesting `http://localhost:<api-port>/alive` returns HTTP 200.
- [ ] Frontend loads with Tailwind styles applied and displays a welcome status header.
