# Quickstart: PR Review Auto-Assignment & PostgreSQL Persistence

**Feature**: `002-pr-review-persistence`
**Date**: 2026-03-08

---

## Prerequisites

- Docker + Docker Compose (for local dev stack)
- .NET 10 SDK
- `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef --version 10.0.3`
- Access to an Azure DevOps organisation

---

## Local Development Setup

### 1. Start the full stack

```bash
docker compose up
```

This starts PostgreSQL (port 5432) and the MeisterProPR API (port 8080). The API applies EF Core migrations automatically on startup.

### 2. Environment variables (dev)

Create a `.env` file at the repository root (never commit):

```env
# Required
MEISTER_ADMIN_KEY=your-admin-key-min-16-chars
DB_CONNECTION_STRING=Host=localhost;Port=5432;Database=meisterpropr;Username=postgres;Password=devpass

# AI review
AI_ENDPOINT=https://your-openai.openai.azure.com/
AI_DEPLOYMENT=gpt-4o
AZURE_CLIENT_ID=...
AZURE_TENANT_ID=...
AZURE_CLIENT_SECRET=...

# Optional: bootstrap initial client keys from old env var (one-time only)
MEISTER_CLIENT_KEYS=key1,key2
```

### 3. Register a client (admin operation)

```bash
curl -X POST http://localhost:8080/clients \
  -H "X-Admin-Key: your-admin-key-min-16-chars" \
  -H "Content-Type: application/json" \
  -d '{"key": "my-client-key-32chars-xxxx", "displayName": "My Team"}'
```

Save the returned `id` — you need it to add crawl configurations.

### 4. Add a crawl configuration

Find your ADO service account's reviewer GUID:
```
GET https://vssps.dev.azure.com/{org}/_apis/profile/profiles/me?api-version=7.1
```
Copy the `id` field.

```bash
curl -X POST http://localhost:8080/clients/{clientId}/crawl-configurations \
  -H "X-Client-Key: my-client-key-32chars-xxxx" \
  -H "Content-Type: application/json" \
  -d '{
    "organizationUrl": "https://dev.azure.com/myorg",
    "projectId": "MyProject",
    "reviewerId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "crawlIntervalSeconds": 60
  }'
```

### 5. Trigger a review

Open any PR in Azure DevOps, go to **Reviewers**, click **Add**, search for the service account, and add it. The backend will detect this on the next crawl cycle (within `crawlIntervalSeconds` seconds) and begin a review.

### 6. Monitor jobs

```bash
# View all jobs (admin)
curl -H "X-Admin-Key: your-admin-key" http://localhost:8080/jobs

# View your own jobs (client)
curl -H "X-Client-Key: my-client-key-32chars-xxxx" http://localhost:8080/reviews
```

---

## Running Tests

```bash
dotnet test
```

Integration tests use `WebApplicationFactory<Program>` with in-memory test repositories — no real database required for unit/integration tests.

For EF Core / PostgreSQL integration tests (Infrastructure layer), Testcontainers spins up a real PostgreSQL container automatically.

---

## Generating Migrations

After changing domain entities or adding new EF models:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/MeisterProPR.Infrastructure \
  --startup-project src/MeisterProPR.Api
```

Commit the generated migration files. They are applied automatically on next startup.

---

## Docker Compose Services

| Service | Port | Notes |
|---------|------|-------|
| `postgres` | 5432 | PostgreSQL 17-alpine; data persisted in `postgres_data` volume |
| `meisterpropr` | 8080 | API; waits for postgres healthcheck before starting |

Health check: `curl http://localhost:8080/healthz`
Metrics: `http://localhost:8080/metrics`
