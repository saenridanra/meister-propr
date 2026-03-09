# Meister ProPR

An AI-powered code review backend for Azure DevOps pull requests.

When a PR is submitted for review, the backend fetches the changed files using an Azure identity,
sends them to an Azure OpenAI model for analysis, and posts the findings back as comment threads
directly on the PR — one thread per finding, anchored to the relevant file and line.

Each registered client can use a **shared global identity** (the backend's own managed identity /
service principal) or store its own **per-client Azure service principal** credentials so that ADO
operations run under that client's identity.

---

## How it works

```
POST /reviews  (from ADO extension or CI)
      │
      ▼
ClientKeyMiddleware  ──  validates X-Client-Key
      │
      ▼
ReviewsController    ──  verifies X-Ado-Token (identity check only), returns 202 + jobId
      │
      ▼
PostgresJobRepository  (persisted in PostgreSQL)
      │
      ▼  polled every 2 s
ReviewJobWorker (BackgroundService)
      │
      ▼
ReviewOrchestrationService
      ├── AdoPullRequestFetcher  ──►  Azure DevOps  (per-client or global identity)
      ├── AgentAiReviewCore      ──►  Azure OpenAI / Foundry  (Responses API)
      └── AdoCommentPoster       ──►  Azure DevOps  (per-client or global identity)
```

The caller's `X-Ado-Token` is used **only** to verify that the caller has access to the ADO
organisation. All ADO operations (fetching PR content, posting comments) use a **backend-controlled**
Azure credential — either the global `DefaultAzureCredential` or the per-client
`ClientSecretCredential` stored for that client.

---

## Project structure

```
src/
├── MeisterProPR.Domain/          # Entities, value objects — zero NuGet deps
├── MeisterProPR.Application/     # Use cases, service interfaces, DTOs
├── MeisterProPR.Infrastructure/  # ADO client, AI client, EF Core repositories
└── MeisterProPR.Api/             # Controllers, middleware, workers, Program.cs
tests/
├── MeisterProPR.Domain.Tests/
├── MeisterProPR.Application.Tests/
├── MeisterProPR.Infrastructure.Tests/
└── MeisterProPR.Api.Tests/
```

Dependency rule: `Api → Application → Domain ← Infrastructure`
Infrastructure references are only wired up in `Program.cs`.

---

## Quick start

```bash
# Set required config (recommended: dotnet user-secrets)
dotnet user-secrets set "AI_ENDPOINT"         "https://<resource>.openai.azure.com/" --project src/MeisterProPR.Api
dotnet user-secrets set "AI_DEPLOYMENT"       "gpt-4o"                               --project src/MeisterProPR.Api
dotnet user-secrets set "MEISTER_CLIENT_KEYS" "my-secret-key"                        --project src/MeisterProPR.Api
dotnet user-secrets set "MEISTER_ADMIN_KEY"   "my-admin-key"                         --project src/MeisterProPR.Api

# Run
dotnet run --project src/MeisterProPR.Api

# Verify
curl http://localhost:5000/healthz
```

See [GETTING_STARTED.md](GETTING_STARTED.md) for full setup instructions, including Azure
permissions, Entra ID app registration, service principal configuration, Docker, and API usage
examples.

---

## Tech stack

| Layer          | Technology                                                  |
|----------------|-------------------------------------------------------------|
| Runtime        | .NET 10 / ASP.NET Core MVC                                  |
| AI client      | `Microsoft.Extensions.AI` + Azure OpenAI Responses API      |
| ADO client     | `Microsoft.TeamFoundationServer.Client`                     |
| Auth           | `Azure.Identity` (`DefaultAzureCredential` / `ClientSecretCredential`) |
| Database       | PostgreSQL 17 via EF Core 10 + Npgsql                       |
| Logging        | Serilog (structured JSON in production)                     |
| Observability  | OpenTelemetry OTLP traces + Prometheus metrics              |
| Tests          | xUnit + NSubstitute + `WebApplicationFactory`               |
| Container      | Linux rootless (`mcr.microsoft.com/dotnet/aspnet:10.0`)     |

---

## Key environment variables

| Variable                    | Required | Description                                                        |
|-----------------------------|----------|--------------------------------------------------------------------|
| `MEISTER_CLIENT_KEYS`       | Yes*     | Comma-separated valid client keys (`X-Client-Key`) — required when `DB_CONNECTION_STRING` is not set |
| `MEISTER_ADMIN_KEY`         | Yes      | Admin API key for `X-Admin-Key` header (client management)        |
| `AI_ENDPOINT`               | Yes      | Azure OpenAI endpoint URL or Azure AI Foundry project URL          |
| `AI_DEPLOYMENT`             | Yes      | Model deployment name (e.g. `gpt-4o`)                              |
| `DB_CONNECTION_STRING`      | No       | PostgreSQL connection string; enables DB mode (persisted jobs + client registry) |
| `PR_CRAWL_INTERVAL_SECONDS` | No       | Polling interval for the PR crawler background worker (default 60) |
| `AZURE_CLIENT_ID`           | Dev      | Service principal app ID (global backend identity)                 |
| `AZURE_TENANT_ID`           | Dev      | Azure AD tenant ID (global backend identity)                       |
| `AZURE_CLIENT_SECRET`       | Dev      | Service principal secret — **never commit**                        |
| `AI_API_KEY`                | No       | AI API key; omit to use `DefaultAzureCredential`                   |

\* When `DB_CONNECTION_STRING` is set, client keys are managed via the `/clients` admin API
and `MEISTER_CLIENT_KEYS` is only used as a bootstrap seed.

---

## Running tests

```bash
dotnet test
```

235 tests across four projects. All tests use in-memory stubs and `WebApplicationFactory` —
no real Azure credentials or database needed. Infrastructure integration tests spin up a
PostgreSQL container automatically via Testcontainers.

---

## Docker

```bash
docker compose up --build
```

The service listens on port `8080` inside the container. Supply variables via a `.env` file at
the repo root (see [GETTING_STARTED.md](GETTING_STARTED.md#running-with-docker)).
