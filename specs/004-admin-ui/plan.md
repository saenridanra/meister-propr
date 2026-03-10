# Implementation Plan: Admin Management UI

**Branch**: `004-admin-ui` | **Date**: 2026-03-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/004-admin-ui/spec.md`

## Summary

Build a Vue 3 single-page application (SPA) served as static files through the existing nginx
proxy that allows administrators to manage backend clients — register, view, edit, enable/disable,
delete, and configure per-client ADO credentials — authenticating with the existing `X-Admin-Key`.
The backend requires no new endpoints; the SPA reuses the existing `/clients` and
`/clients/{id}/ado-credentials` admin API. CORS is handled by extending the existing
`CORS_ORIGINS` env-var mechanism for local dev; in Docker the SPA is co-served through nginx
(same origin).

## Technical Context

**Language/Version**: TypeScript 5.x + Vue 3.5 (Composition API, `<script setup>`)
**Primary Dependencies**: Vite 6, Vue Router 4, Vitest 3, `@vue/test-utils` 2, `openapi-typescript` (dev, code-gen), `openapi-fetch` (runtime typed client)
**Storage**: No backend storage changes — reads/writes existing `clients` table via REST API
**Testing**: Vitest + `@vue/test-utils` (component tests); existing xUnit + NSubstitute for backend
**Target Platform**: Modern desktop browsers (Chrome, Firefox, Edge, Safari); Linux rootless container
**Project Type**: Frontend SPA (`admin-ui/` directory at repo root) + minor backend config change (CORS)
**Performance Goals**: Client list renders within 2 s for 500 clients; login response under 1 s
**Constraints**: Admin secret stored in `sessionStorage` only (never `localStorage`); ADO secret never rendered; no new backend endpoints
**Scale/Scope**: Single admin user, up to ~500 clients, small bundle (<500 KB gzipped)

## Constitution Check

- [X] **I. API-Contract-First** — No new backend endpoints; `openapi.json` unchanged. The SPA calls
  existing `/clients` and `/clients/{id}/ado-credentials` endpoints. CORS config change is env-var
  only (`CORS_ORIGINS`) — no contract change.
- [X] **II. Test-First** — `[TEST]` tasks defined first in `tasks.md`. Vitest component tests written
  before component implementation (Red → Green → Refactor). Backend tests unchanged.
- [X] **III. Container-First** — `admin-ui/Dockerfile` uses multi-stage build (Node build + nginx:alpine).
  Runtime API base URL injected via `VITE_API_BASE_URL` build arg. Served through existing nginx in
  Docker (same-origin — no CORS needed). `/healthz` unaffected.
- [X] **IV. Clean Architecture** — SPA is a separate project with its own layered structure
  (`views/` → `composables/` → `services/`). No backend architecture changes. New SPA project
  justified in Complexity Tracking.
- [X] **V. Security** — Admin key stored in `sessionStorage`, sent in `X-Admin-Key` header only.
  Never logged client-side or server-side. ADO secret is a write-only form field (`type="password"`),
  never read back or displayed. Backend already scrubs sensitive headers from logs.
- [X] **VI. Job Reliability** — N/A: no new background job types.
- [X] **VII. Observability** — N/A: frontend SPA, no server-side logging. Backend CORS change
  has no observability impact.

## Project Structure

### Documentation (this feature)

```text
specs/004-admin-ui/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
admin-ui/                          # Vue 3 SPA — new top-level project
├── src/
│   ├── components/
│   │   ├── AppHeader.vue          # Logo + logout button
│   │   ├── ClientTable.vue        # Filterable client list table
│   │   ├── ClientForm.vue         # Create / edit client form
│   │   ├── AdoCredentialsForm.vue # Set per-client ADO credentials
│   │   └── ConfirmDialog.vue      # Reusable "are you sure?" modal
│   ├── views/
│   │   ├── LoginView.vue          # Admin key entry screen
│   │   ├── ClientsView.vue        # Client list + search + create
│   │   └── ClientDetailView.vue   # Edit + delete + ADO credentials
│   ├── composables/
│   │   └── useSession.ts          # Admin key sessionStorage read/write/clear
│   ├── services/
│   │   ├── api.ts                 # Thin wrapper: creates openapi-fetch client with X-Admin-Key middleware + login override
│   │   └── generated/
│   │       └── openapi.ts         # Auto-generated from ../../openapi.json — DO NOT EDIT MANUALLY
│   ├── router/
│   │   └── index.ts               # Vue Router: /login ↔ / (clients) ↔ /:id
│   ├── types/
│   │   └── index.ts               # Shared TypeScript interfaces (Client, AdoCredentials…)
│   ├── App.vue
│   └── main.ts
├── tests/
│   ├── components/                # Vitest + @vue/test-utils component tests
│   └── services/                  # Vitest unit tests for api.ts
├── public/
│   └── favicon.ico
├── index.html
├── vite.config.ts
├── tsconfig.json
├── tsconfig.node.json
├── package.json
└── Dockerfile                     # Multi-stage: Node build → nginx:alpine serve

nginx/
└── nginx.conf                     # UPDATED: add location /admin/ → admin-ui service

docker-compose.yml                 # UPDATED: add admin-ui service

src/MeisterProPR.Api/Program.cs    # CORS_ORIGINS already extensible — no code change needed
                                   # (document: add http://localhost:5173 for local Vite dev)
```

**Structure Decision**: The SPA lives in `admin-ui/` at repo root, separate from the .NET solution.
This keeps the two stacks clearly separated without a monorepo tool. The existing nginx handles SSL
termination and routes `/admin/` requests to the `admin-ui` Docker service, keeping both SPA and API
reachable on the same `https://localhost:5443` origin in Docker (no CORS needed). For local Vite dev
(`npm run dev`, port 5173), the Vite dev server proxies API calls (`/clients`, `/reviews`, etc.)
to the backend, also avoiding CORS for the most common dev workflow. The `CORS_ORIGINS` env var
remains available as a fallback for non-Vite deployments.

## Complexity Tracking

| Complexity                   | Why Needed                                                        | Simpler Alternative Rejected Because                                      |
|------------------------------|-------------------------------------------------------------------|---------------------------------------------------------------------------|
| New top-level SPA project    | Spec requires a browser-based management UI; can't be served as backend views without coupling front and back ends | Razor Pages would couple backend to presentation layer — violates Clean Architecture |
| nginx routing update         | SPA must be co-served with the API for same-origin in production  | Separate port/domain would require CORS config and complicate local dev |
| Vite dev proxy               | Enables local dev without CORS changes to the backend             | Adding `localhost:5173` to fixed CORS origins would pollute production config |
