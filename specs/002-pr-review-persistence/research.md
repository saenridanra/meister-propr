# Research: PR Review Auto-Assignment & PostgreSQL Persistence

**Feature**: `002-pr-review-persistence`
**Date**: 2026-03-08

---

## Decision 1: PostgreSQL ORM — EF Core 10 + Npgsql

**Decision**: Use `Microsoft.EntityFrameworkCore` v10.0.3 + `Npgsql.EntityFrameworkCore.PostgreSQL` v10.0.0.

**Rationale**: EF Core 10 is the current LTS release aligned with .NET 10. Npgsql 10.0.0 is the matching provider (released Nov 2025). The existing `IJobRepository` interface lives in Application; we only add a new Infrastructure implementation — no business logic changes.

**Packages (add to MeisterProPR.Infrastructure.csproj)**:
| Package | Version |
|---------|---------|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.0 |
| `Microsoft.EntityFrameworkCore` | 10.0.3 |
| `Microsoft.EntityFrameworkCore.Relational` | 10.0.3 |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.3 (PrivateAssets="All") |

**Global CLI tool** (for migration generation in dev):
```bash
dotnet tool install --global dotnet-ef --version 10.0.3
```

**Alternatives considered**:
- Dapper: rejected — no type-safe migrations or JSONB value-object mapping
- SQLite: rejected — not a production store; spec calls for PostgreSQL
- EF Core 9: rejected — version skew with .NET 10 stack

---

## Decision 2: ReviewResult Storage — Separate Columns + JSONB

**Decision**: Store `ReviewResult.Summary` as a plain TEXT column (`result_summary`). Store `ReviewResult.Comments` (array of `ReviewComment`) as a JSONB column (`result_comments`) using EF Core Fluent API `OwnsOne(...).ToJson()`. No attributes on domain entities.

**Rationale**: `result_summary` as a dedicated column makes it readable in dashboards and SQL queries without JSON path syntax. The `ReviewComment[]` array has no fixed schema size, making JSONB the natural fit. EF Core 10's `OwnsOne(...).ToJson()` handles serialisation transparently with Npgsql's native JSONB support.

**Alternatives considered**:
- Separate `review_comments` table with FK: rejected — over-engineered; each job has exactly one result
- Store entire `ReviewResult` as a single JSONB blob: workable but `result_summary` would need JSON path queries

---

## Decision 3: EF Core Migration Strategy — Committed Migrations + Startup Apply

**Decision**: Code-first migrations committed to `src/MeisterProPR.Infrastructure/Data/Migrations/`. Applied via `DbContext.Database.MigrateAsync()` in `Program.cs` on startup (before the app starts accepting requests). Idempotent.

**Rationale**: Single-instance deployment; startup migration is safe and operationally simple. The docker-compose dev stack adds a PostgreSQL healthcheck so the app container waits for postgres readiness before attempting migration.

**Generating migrations** (developer workflow):
```bash
dotnet ef migrations add <MigrationName> \
  --project src/MeisterProPR.Infrastructure \
  --startup-project src/MeisterProPR.Api
```

**Alternatives considered**:
- Migration bundle in separate init container: safer for multi-replica, rejected as over-engineered for current scale
- SQL script generation in CI: adds deployment step complexity not yet needed
- `EnsureCreated()`: bypasses migrations entirely — rejected

---

## Decision 4: Multi-Client Database Design (Clients + CrawlConfigurations)

**Decision**: Store client registrations and crawl targets in the database. Replace env-var-only `IClientRegistry` with a database-backed implementation. Add a `CrawlConfiguration` entity linking clients to ADO organisations/projects.

**Rationale**: The user explicitly stated that clients and their crawl organisations should be stored in the database, not hardcoded in environment variables. This enables:
- Adding new clients without restarting the service
- Assigning multiple crawl targets per client
- All crawl configuration surviving restarts

**New tables**: `clients`, `crawl_configurations` (see data-model.md).

**Migration path for existing deployments**: On first startup, if `MEISTER_CLIENT_KEYS` env var is set and the `clients` table is empty, seed the database with those keys as active clients (one-time bootstrap).

**Alternatives considered**:
- Keep env-var `IClientRegistry` and add separate crawl config DB: rejected — split storage is confusing; all config should live in one place
- Admin UI for client management: out of scope for this feature; management goes through the new API endpoints

---

## Decision 5: ADO PR Discovery — GitHttpClient with ReviewerId Filter

**Decision**: Use `GitHttpClient.GetPullRequestsAsync(projectId, repositoryId: null, criteria)` where `criteria.ReviewerId = Guid.Parse(crawlConfig.ReviewerId)` and `criteria.Status = PullRequestStatus.Active`. Passing `null` for `repositoryId` returns PRs across all repositories in the project.

**Rationale**: The existing `VssConnectionFactory` already provides `VssConnection`; `GetClient<GitHttpClient>()` is the established pattern in `AdoPullRequestFetcher`. The reviewer ID is a Guid (the ADO identity ID of the service account, stored per-client in `CrawlConfiguration.ReviewerId`).

**Latest iteration ID**: Retrieved via `GetPullRequestIterationsAsync()` → `Max(i => i.Id)`.

**Alternatives considered**:
- Filter on display name / unique name: rejected — string matching is fragile; Guid is reliable
- Polling all repos individually: rejected — project-level search is a single API call

---

## Decision 6: New Interface — IAssignedPullRequestFetcher

**Decision**: Add `IAssignedPullRequestFetcher` to Application interfaces. It takes a crawl configuration DTO and returns a list of `AssignedPullRequestRef` records.

```csharp
public interface IAssignedPullRequestFetcher
{
    Task<IReadOnlyList<AssignedPullRequestRef>> GetAssignedOpenPullRequestsAsync(
        CrawlConfigurationDto config, CancellationToken ct);
}

public sealed record AssignedPullRequestRef(
    string OrganizationUrl,
    string ProjectId,
    string RepositoryId,
    int PullRequestId,
    int LatestIterationId);
```

**Implementation**: `AdoAssignedPrFetcher` in Infrastructure, following `AdoPullRequestFetcher` patterns.

---

## Decision 7: Crawler Architecture — Dedicated BackgroundService per Crawl Cycle

**Decision**: Add `AdoPrCrawlerWorker : BackgroundService` in the Api layer. It periodically invokes `PrCrawlService` (Application layer) which: (1) loads all active `CrawlConfiguration` records, (2) for each: fetches assigned PRs via `IAssignedPullRequestFetcher`, (3) for each PR: checks `IJobRepository.FindActiveJob`, (4) creates a new `ReviewJob` if none exists.

**Rationale**: Separating the crawler worker from `ReviewJobWorker` keeps concerns isolated. `ReviewJobWorker` processes pending jobs; `AdoPrCrawlerWorker` discovers new ones.

---

## Decision 8: ReviewJob.ClientKey Nullability

**Decision**: Make `ReviewJob.ClientKey` nullable (`string?`). Crawler-created jobs have `ClientKey = null` (system-initiated; no API caller). The `GET /reviews` endpoint continues filtering by client key — callers only see their own jobs. A new `GET /jobs` endpoint returns all jobs regardless of client key.

**Alternatives considered**:
- Sentinel `"__system__"` client key: rejected — leaks internal detail through the API
- Foreign key to `Client.Id` on `ReviewJob`: useful long-term but requires domain entity change; nullable string key is sufficient for now and matches existing code

---

## Decision 9: New Environment Variables

| Variable | Purpose | Required | Default |
|----------|---------|----------|---------|
| `DB_CONNECTION_STRING` | PostgreSQL connection string | Yes | — |
| `PR_CRAWL_INTERVAL_SECONDS` | Global crawl polling interval | No | `60` |
| `MEISTER_CLIENT_KEYS` | Bootstrap keys (only read if `clients` table is empty on startup) | No | — |

The per-client `ADO_CRAWL_ORG_URL` / `ADO_CRAWL_PROJECT_ID` / `ADO_REVIEWER_ID` vars from prior design are **removed** — these are stored in `crawl_configurations` table per client.

---

## Decision 10: No Changes to IJobRepository Interface

**Decision**: `IJobRepository` interface stays unchanged. `PostgresJobRepository` implements all existing methods identically. `FindActiveJob` already provides the idempotency check needed by the crawler.

**Rationale**: Clean architecture — Application layer is isolated from storage technology. Swapping the implementation is precisely what Constitution Principle IV prescribes.
