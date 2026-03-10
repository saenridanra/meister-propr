# Quickstart: Admin Management UI (004-admin-ui)

**Feature**: 004-admin-ui | **Date**: 2026-03-09

---

## Prerequisites

- Node.js 22 LTS + npm 10 (or later)
- The Meister ProPR backend running locally (see `GETTING_STARTED.md`)
- Docker (for the full stack scenario)

---

## Local Development

### 1 — Install dependencies

```bash
cd admin-ui
npm install
```

### 2 — Configure the dev server

Create `admin-ui/.env.local` (git-ignored):

```env
# Point Vite proxy at the running backend.
# If using nginx (https://localhost:5443), set:
VITE_API_BASE_URL=http://localhost:8080

# Leave empty to use Vite proxy (recommended — avoids CORS):
# VITE_API_BASE_URL=
```

The Vite dev server is pre-configured to proxy `/clients`, `/reviews`, `/identities`, and
`/healthz` to `http://localhost:8080` (the backend's plain HTTP port) so no CORS changes
are needed for local development.

### 3 — Start the dev server

```bash
npm run dev
```

The SPA is available at `http://localhost:5173`.

### 4 — Run the test suite

```bash
npm test           # run all tests once
npm run test:watch # watch mode
```

---

## Docker (Full Stack)

The `docker-compose.yml` at the repo root includes an `admin-ui` service that builds the SPA
and serves it via nginx on port 80 inside the container. The existing nginx SSL proxy routes
`https://localhost:5443/admin/` to the `admin-ui` service.

```bash
docker compose up --build
```

Open `https://localhost:5443/admin/` in your browser.

---

## Using the Admin Interface

### 1 — Log in

Navigate to the admin interface. Enter the `MEISTER_ADMIN_KEY` value from your `.env` file.

### 2 — Register a client

Click **New Client**, enter a display name and a secret key, and click **Create**.
The key is the value callers will put in the `X-Client-Key` header.

### 3 — Set per-client ADO credentials (optional)

Click on a client, open the **ADO Credentials** section, enter the Azure service principal
`tenantId`, `clientId`, and `secret`, then click **Save**.

The secret is write-only — it is never displayed after saving.

### 4 — Add a crawl configuration

After registering a client, use the backend API (or Insomnia) to add a crawl configuration:

```bash
curl -k -X POST https://localhost:5443/clients/<client-id>/crawl-configurations \
  -H "Content-Type: application/json" \
  -H "X-Client-Key: <client-key>" \
  -d '{
    "organizationUrl": "https://dev.azure.com/my-org",
    "projectId": "my-project",
    "reviewerDisplayName": "My Service Principal",
    "crawlIntervalSeconds": 60
  }'
```

> Crawl configuration management is not included in the admin UI in this release.

---

## Key Files

| File                                  | Purpose                                               |
|---------------------------------------|-------------------------------------------------------|
| `admin-ui/src/services/api.ts`        | Typed fetch wrapper; injects `X-Admin-Key`            |
| `admin-ui/src/composables/useSession.ts` | Admin key `sessionStorage` read/write/clear        |
| `admin-ui/src/router/index.ts`        | Route definitions + navigation guard (auth check)     |
| `admin-ui/vite.config.ts`             | Vite config including dev-server proxy                |
| `admin-ui/Dockerfile`                 | Multi-stage build: Node → nginx:alpine                |
| `nginx/nginx.conf`                    | Updated: `/admin/` routes to `admin-ui` service       |
| `docker-compose.yml`                  | Updated: `admin-ui` service added                     |
