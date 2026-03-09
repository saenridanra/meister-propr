# Implementation Plan: PR Review Auto-Assignment & PostgreSQL Persistence

**Branch**: `002-pr-review-persistence` | **Date**: 2026-03-08 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-pr-review-persistence/spec.md`

## Summary

Replace the in-memory `ConcurrentDictionary`-backed `IJobRepository` with a PostgreSQL-backed implementation using EF Core 10 + Npgsql. Introduce a `Client` entity and `CrawlConfiguration` entity (persisted in PostgreSQL) so that client registrations and ADO crawl targets survive restarts and are manageable per-client. Add `AdoPrCrawlerWorker` — a new background service that periodically discovers open ADO pull requests where the service account is listed as a reviewer and creates pending `ReviewJob` records for unreviewed PRs. Add admin-only endpoints for client management and a global job-list endpoint.

## Technical Context

**Language/Version**: C# / .NET 10, TFM `net10.0`
**Primary Dependencies**: ASP.NET Core MVC, EF Core 10.0.3, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0, Microsoft.TeamFoundationServer.Client 20.269.0-preview (existing)
**Storage**: PostgreSQL 17 via EF Core (replaces in-memory ConcurrentDictionary)
**Testing**: xUnit + NSubstitute (unit), WebApplicationFactory (integration), Testcontainers.PostgreSql (EF repo integration tests)
**Target Platform**: Linux rootless container (existing)
**Project Type**: Web service (ASP.NET Core)
**Performance Goals**: Job history endpoint < 2 s for 10,000 records; `/healthz` < 200 ms
**Constraints**: No Windows APIs; all config via env vars; connection string never logged; `X-Admin-Key` never logged
**Scale/Scope**: Single deployment; multiple clients with multiple crawl targets each

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **I. API-Contract-First** — New non-breaking endpoints added: `POST/GET/PATCH /clients`, `POST/GET/PATCH /clients/{id}/crawl-configurations`, `GET /jobs`. All carry full XML docs. `openapi.json` must be regenerated and committed. No existing endpoint paths or schemas change.
- [ ] **II. Test-First** — `[TEST]` tasks will lead `tasks.md`. Confirmed failing before implementation begins (enforced by speckit workflow).
- [x] **III. Container-First** — No Windows-specific APIs. `DB_CONNECTION_STRING` and `MEISTER_ADMIN_KEY` via env vars. PostgreSQL added to `docker-compose.yml`. `/healthz` extended to check DB connectivity.
- [x] **IV. Clean Architecture** — `IJobRepository`, `IClientRegistry`, new `ICrawlConfigurationRepository`, new `IAssignedPullRequestFetcher` all defined in Application. EF Core `DbContext` lives in Infrastructure. `AdoPrCrawlerWorker` lives in Api. DI wiring only in `Program.cs` and `InfrastructureServiceExtensions`.
- [x] **V. Security** — New `X-Admin-Key` middleware for admin routes. `X-Client-Key` validation preserved. Connection string, admin key, client keys NEVER logged. Serilog destructuring policies extended to scrub `X-Admin-Key`.
- [x] **VI. Job Reliability** — `IJobRepository` interface unchanged. `PostgresJobRepository` implements all same state transitions. Idempotency via `FindActiveJob`. Concurrent-safe job creation via database unique constraint check + application-level guard. Crawler-created jobs use nullable `ClientKey`.
- [x] **VII. Observability** — DB calls wrapped in Activity spans. New `AdoPrCrawlerWorker` loop wrapped in Activity. Prometheus metric for crawl cycle duration and discovered-PRs count. `/healthz` extended to verify DB reachability.

## Project Structure

### Documentation (this feature)

```text
specs/002-pr-review-persistence/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── api-additions.md # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code Changes

```text
src/
├── MeisterProPR.Domain/
│   └── Entities/
│       └── ReviewJob.cs                         # CHANGE: ClientKey string → string?
│
├── MeisterProPR.Application/
│   ├── Interfaces/
│   │   ├── IClientRegistry.cs                   # EXISTING (unchanged interface)
│   │   ├── IJobRepository.cs                    # EXISTING (unchanged interface)
│   │   ├── IAssignedPullRequestFetcher.cs       # NEW
│   │   └── ICrawlConfigurationRepository.cs     # NEW
│   ├── DTOs/
│   │   ├── AssignedPullRequestRef.cs            # NEW (record)
│   │   └── CrawlConfigurationDto.cs             # NEW (record)
│   └── Services/
│       └── PrCrawlService.cs                    # NEW
│
├── MeisterProPR.Infrastructure/
│   ├── Data/
│   │   ├── MeisterProPRDbContext.cs             # NEW
│   │   ├── Configurations/
│   │   │   ├── ReviewJobEntityTypeConfiguration.cs  # NEW
│   │   │   ├── ClientEntityTypeConfiguration.cs     # NEW
│   │   │   └── CrawlConfigurationEntityTypeConfiguration.cs  # NEW
│   │   ├── Models/
│   │   │   ├── ClientRecord.cs                  # NEW (EF entity, not a domain entity)
│   │   │   └── CrawlConfigurationRecord.cs      # NEW (EF entity)
│   │   └── Migrations/                          # NEW (generated by dotnet-ef)
│   ├── Repositories/
│   │   ├── InMemoryJobRepository.cs             # KEEP (used in integration tests)
│   │   ├── PostgresJobRepository.cs             # NEW
│   │   ├── PostgresClientRegistry.cs            # NEW (implements IClientRegistry)
│   │   └── PostgresCrawlConfigurationRepository.cs  # NEW (implements ICrawlConfigurationRepository)
│   ├── AzureDevOps/
│   │   └── AdoAssignedPrFetcher.cs              # NEW (implements IAssignedPullRequestFetcher)
│   ├── DependencyInjection/
│   │   └── InfrastructureServiceExtensions.cs   # CHANGE: register new services
│   └── MeisterProPR.Infrastructure.csproj       # CHANGE: add EF Core + Npgsql packages
│
└── MeisterProPR.Api/
    ├── Controllers/
    │   ├── ReviewsController.cs                  # EXISTING (unchanged)
    │   ├── ClientsController.cs                  # NEW (admin + client endpoints)
    │   └── JobsController.cs                     # NEW (GET /jobs — admin)
    ├── Workers/
    │   ├── ReviewJobWorker.cs                    # EXISTING (unchanged)
    │   └── AdoPrCrawlerWorker.cs                 # NEW
    ├── Middleware/
    │   ├── ClientKeyMiddleware.cs                # EXISTING (unchanged)
    │   └── AdminKeyMiddleware.cs                 # NEW
    ├── Program.cs                                # CHANGE: register DbContext, new services, migrations
    ├── appsettings.json                          # CHANGE: add DB config section placeholder
    └── MeisterProPR.Api.csproj                  # CHANGE: add EF Core Design (tools)

tests/
├── MeisterProPR.Domain.Tests/
│   └── Entities/ReviewJobTests.cs               # CHANGE: update for nullable ClientKey
│
├── MeisterProPR.Application.Tests/
│   └── Services/
│       ├── ReviewOrchestrationServiceTests.cs    # EXISTING
│       └── PrCrawlServiceTests.cs               # NEW
│
├── MeisterProPR.Infrastructure.Tests/
│   └── Repositories/
│       ├── InMemoryJobRepositoryTests.cs         # EXISTING
│       └── PostgresJobRepositoryTests.cs         # NEW (Testcontainers)
│
└── MeisterProPR.Api.Tests/
    ├── Controllers/
    │   ├── ClientsControllerTests.cs             # NEW
    │   └── JobsControllerTests.cs               # NEW
    ├── Workers/
    │   └── AdoPrCrawlerWorkerTests.cs            # NEW
    └── Middleware/
        └── AdminKeyMiddlewareTests.cs            # NEW
```

## Complexity Tracking

No constitution violations. All complexity is justified by the spec:

| Addition | Why Needed | Simpler Alternative Rejected Because |
|----------|-----------|--------------------------------------|
| `MeisterProPRDbContext` | EF Core requires a DbContext; clean arch places it in Infrastructure | Direct SQL (Dapper) loses type-safe migrations and JSONB mapping |
| `ClientRecord` / `CrawlConfigurationRecord` (EF models separate from domain) | `Client` and `CrawlConfiguration` have no domain behaviour; they are pure data | Adding them to Domain would violate the "zero NuGet deps" rule in Domain |
| `AdminKeyMiddleware` (separate from `ClientKeyMiddleware`) | Admin endpoints have different auth semantics | Adding `isAdmin` flag to client registry conflates two auth scopes |

## Implementation Notes

### Phase A — Domain & Application (pure logic, no I/O)

1. **Domain change**: `ReviewJob.ClientKey` → `string?`. Update constructor/init.
2. **New Application interfaces**: `IAssignedPullRequestFetcher`, `ICrawlConfigurationRepository`.
3. **New Application DTOs**: `AssignedPullRequestRef`, `CrawlConfigurationDto`.
4. **New Application service**: `PrCrawlService.CrawlAsync(CancellationToken)`:
   - Load all active `CrawlConfiguration` records via `ICrawlConfigurationRepository`
   - For each: call `IAssignedPullRequestFetcher.GetAssignedOpenPullRequestsAsync(config, ct)`
   - For each returned PR ref: call `IJobRepository.FindActiveJob(...)` — if null, create and `Add` a new `ReviewJob` with `ClientKey = null`

### Phase B — Infrastructure (EF Core + Npgsql)

1. Add NuGet packages to `MeisterProPR.Infrastructure.csproj`.
2. Create `MeisterProPRDbContext` with `DbSet<ReviewJob>`, `DbSet<ClientRecord>`, `DbSet<CrawlConfigurationRecord>`.
3. Fluent API configurations — no attributes on domain entities.
4. Implement `PostgresJobRepository` (all `IJobRepository` methods via EF Core).
5. Implement `PostgresClientRegistry` (replaces `EnvVarClientRegistry`; checks `clients` table).
6. Implement `PostgresCrawlConfigurationRepository`.
7. Implement `AdoAssignedPrFetcher` (uses existing `VssConnectionFactory` + `GitHttpClient`).
8. Generate initial EF Core migration.
9. Bootstrap logic: on startup, if `clients` table is empty and `MEISTER_CLIENT_KEYS` is set → seed clients.

### Phase C — Api Layer

1. **`AdminKeyMiddleware`**: validates `X-Admin-Key` against `MEISTER_ADMIN_KEY` env var; applied only to admin routes.
2. **`ClientsController`**: `POST /clients`, `GET /clients`, `PATCH /clients/{id}` (admin-protected).
3. **Crawl config sub-resource**: `POST/GET/PATCH /clients/{clientId}/crawl-configurations` (client-key protected, ownership check).
4. **`JobsController`**: `GET /jobs` with pagination (admin-protected).
5. **`AdoPrCrawlerWorker`**: `BackgroundService` with `PeriodicTimer`; interval = minimum of all active `CrawlConfiguration.CrawlIntervalSeconds`; calls `PrCrawlService.CrawlAsync(ct)` per cycle.
6. **`Program.cs`**: register `MeisterProPRDbContext`, `PostgresJobRepository`, `PostgresClientRegistry`, `PostgresCrawlConfigurationRepository`, `AdoAssignedPrFetcher`, `AdoPrCrawlerWorker`; call `db.Database.MigrateAsync()` before `app.Run()`.
7. **`docker-compose.yml`**: add `postgres` service with healthcheck; add `DB_CONNECTION_STRING` and `MEISTER_ADMIN_KEY` to `meisterpropr` service env; app `depends_on: postgres: condition: service_healthy`.
8. **`/healthz`**: extend to check DB connectivity.

### Key Constraints

- `InMemoryJobRepository` is **kept** and used by `WebApplicationFactory` integration tests — tests do not need a real database.
- `PostgresJobRepository` integration tests use `Testcontainers.PostgreSql` (add to `MeisterProPR.Infrastructure.Tests.csproj`).
- `TryTransition` in `PostgresJobRepository` uses EF Core optimistic concurrency (row version / `xmin` in PostgreSQL) for atomic state transitions.
- ADO token (`X-Ado-Token`) handling is unchanged — it still flows only to the ADO API.
- `X-Admin-Key` and `DB_CONNECTION_STRING` are added to Serilog destructuring scrub list.
