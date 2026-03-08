# Tasks: PR Review Auto-Assignment & PostgreSQL Persistence

**Input**: Design documents from `/specs/002-pr-review-persistence/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/api-additions.md ✓

**Tests**: Constitution Principle II — TDD is mandatory. `[TEST]` tasks appear first in every phase and MUST be confirmed failing before implementation begins.

**Organization**: Tasks grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on other in-progress tasks)
- **[US#]**: Which user story this task belongs to
- **[TEST]**: Write first — must fail before implementation begins

## Path Conventions

- **API layer**: `src/MeisterProPR.Api/`
- **Application layer**: `src/MeisterProPR.Application/`
- **Domain layer**: `src/MeisterProPR.Domain/`
- **Infrastructure layer**: `src/MeisterProPR.Infrastructure/`
- **Tests**: `tests/MeisterProPR.{Layer}.Tests/`

---

## Phase 1: Setup (EF Core & PostgreSQL Infrastructure)

**Purpose**: Add packages, define EF models, generate migration, update docker-compose. Pure scaffolding — no business logic.

- [X] T001 Add EF Core + Npgsql packages to `src/MeisterProPR.Infrastructure/MeisterProPR.Infrastructure.csproj`: `Microsoft.EntityFrameworkCore` 10.0.3, `Microsoft.EntityFrameworkCore.Relational` 10.0.3, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.0, `Microsoft.EntityFrameworkCore.Design` 10.0.3 (PrivateAssets="All")
- [X] T002 [P] Create EF persistence models `src/MeisterProPR.Infrastructure/Data/Models/ClientRecord.cs` and `src/MeisterProPR.Infrastructure/Data/Models/CrawlConfigurationRecord.cs` (plain C# classes, no domain logic, no EF attributes — properties match data-model.md column specs)
- [X] T003 Create `src/MeisterProPR.Infrastructure/Data/MeisterProPRDbContext.cs` with `DbSet<ReviewJob> ReviewJobs`, `DbSet<ClientRecord> Clients`, `DbSet<CrawlConfigurationRecord> CrawlConfigurations`; call `ApplyConfigurationsFromAssembly` in `OnModelCreating`
- [X] T004 [P] Create Fluent API type configurations in `src/MeisterProPR.Infrastructure/Data/Configurations/`: `ReviewJobEntityTypeConfiguration.cs`, `ClientEntityTypeConfiguration.cs`, `CrawlConfigurationEntityTypeConfiguration.cs` — map all columns per data-model.md; store `ReviewResult.Comments` as JSONB via `OwnsOne(...).ToJson()`; add all indexes
- [X] T005 Generate initial EF Core migration: `dotnet ef migrations add InitialCreate --project src/MeisterProPR.Infrastructure --startup-project src/MeisterProPR.Api` — commit migration files
- [X] T006 Update `docker-compose.yml`: add `postgres:17-alpine` service with `pg_isready` health check, `postgres_data` named volume; add `DB_CONNECTION_STRING` and `MEISTER_ADMIN_KEY` env vars to `meisterpropr` service; add `depends_on: postgres: condition: service_healthy`; add `ConnectionStrings` and `AdminKey` placeholder sections to `src/MeisterProPR.Api/appsettings.json`

**Checkpoint**: `dotnet build` passes; `docker compose up` starts postgres. Domain entity change (`ClientKey` → nullable) is deferred to Phase 2 — test T012 must be written and confirmed failing first.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core repository and middleware implementations. ALL `[TEST]` tasks must be written and confirmed failing before any implementation task in this phase begins.

**⚠️ CRITICAL**: No user story work until this phase is complete.

### Tests (write first — confirm failing before T007 domain change and before T016)

- [X] T008 [P] Add `Testcontainers.PostgreSql` 4.x NuGet to `tests/MeisterProPR.Infrastructure.Tests/MeisterProPR.Infrastructure.Tests.csproj`
- [X] T009 [P] [TEST] Write failing tests in `tests/MeisterProPR.Infrastructure.Tests/Repositories/PostgresJobRepositoryTests.cs` using `Testcontainers.PostgreSql`: cover `Add`, `GetById`, `GetPendingJobs` (ordered oldest-first), `GetAllForClient`, `FindActiveJob` (returns null for Failed; returns job for Pending/Processing/Completed), `TryTransition` (CAS — concurrent call returns false), `SetResult`, `SetFailed`
- [X] T010 [P] [TEST] Write failing tests in `tests/MeisterProPR.Infrastructure.Tests/Repositories/PostgresClientRegistryTests.cs`: cover `IsValidAsync` returns true for active key, false for inactive/unknown; bootstrap from `MEISTER_CLIENT_KEYS` when table is empty
- [X] T011 [P] [TEST] Write failing tests in `tests/MeisterProPR.Api.Tests/Middleware/AdminKeyMiddlewareTests.cs`: 401 for missing `X-Admin-Key`, 401 for wrong key, passes through for correct key; non-admin routes are unaffected
- [X] T012 [P] [TEST] Update `tests/MeisterProPR.Domain.Tests/Entities/ReviewJobTests.cs` — add tests that construct a `ReviewJob` with `ClientKey = null` (assert valid; crawler-initiated job)

### Domain Change (C1 fix — must follow T012 failing)

- [X] T007 Update `src/MeisterProPR.Domain/Entities/ReviewJob.cs` — change `ClientKey` from `string` to `string?` (nullable); update any non-null assertions or constructor guards; set `processing_started_at` (`DateTimeOffset? ProcessingStartedAt`) property (init null, set on transition to Processing); **confirm T012 fails before applying this task**

### Application Interfaces

- [X] T013 [P] Create `src/MeisterProPR.Application/Interfaces/IAssignedPullRequestFetcher.cs` and `src/MeisterProPR.Application/DTOs/AssignedPullRequestRef.cs` (record: `OrganizationUrl`, `ProjectId`, `RepositoryId`, `PullRequestId`, `LatestIterationId`)
- [X] T014 [P] Create `src/MeisterProPR.Application/Interfaces/ICrawlConfigurationRepository.cs` (methods: `GetAllActiveAsync`, `AddAsync`, `GetByClientAsync`, `SetActiveAsync`) and `src/MeisterProPR.Application/DTOs/CrawlConfigurationDto.cs`
- [X] T044 [P] Extend `src/MeisterProPR.Application/Interfaces/IClientRegistry.cs` with `GetClientIdByKeyAsync(string key, CancellationToken ct = default) → Task<Guid?>` — returns the client's UUID for a valid active key, `null` for unknown/inactive (A1 fix: needed by ownership check in ClientsController)
- [X] T045 [P] Extend `src/MeisterProPR.Application/Interfaces/IJobRepository.cs` with two new methods: (a) `GetAllJobsAsync(int limit, int offset, JobStatus? status, CancellationToken ct = default) → Task<(int total, IReadOnlyList<ReviewJob> items)>` returning all jobs newest-first; (b) `GetProcessingJobsAsync(CancellationToken ct = default) → Task<IReadOnlyList<ReviewJob>>` returning only Processing-status jobs (A2/A3 fix)

### Tests for New Interface Methods (write before implementations)

- [X] T046 [P] [TEST] Add failing unit test for `IClientRegistry.GetClientIdByKeyAsync` to `tests/MeisterProPR.Infrastructure.Tests/Repositories/PostgresClientRegistryTests.cs`: correct `Guid` returned for valid active key; `null` for unknown key; `null` for inactive key (A1 test)
- [X] T047 [P] [TEST] Add failing tests for new `IJobRepository` methods to `tests/MeisterProPR.Infrastructure.Tests/Repositories/PostgresJobRepositoryTests.cs`: `GetAllJobsAsync` returns all jobs across client keys newest-first with correct total count; pagination `limit`/`offset` works; `status` filter returns only matching jobs; `GetProcessingJobsAsync` returns only Processing jobs; returns empty list when none (A2/A3 tests)

### Infrastructure Implementations

- [X] T015 [P] Implement `src/MeisterProPR.Infrastructure/AzureDevOps/StubAssignedPrFetcher.cs` (no-op: returns empty list; registered in dev mode alongside existing stubs)
- [X] T016 Implement `src/MeisterProPR.Infrastructure/Repositories/PostgresJobRepository.cs` (all `IJobRepository` methods via EF Core; `TryTransition` uses optimistic concurrency — load + status check + save; `GetPendingJobs` orders by `SubmittedAt` ascending)
- [X] T017 Implement `src/MeisterProPR.Infrastructure/Repositories/PostgresClientRegistry.cs` (implements `IClientRegistry`; `IsValidAsync` queries `clients` table by key where `is_active = true`; implement `GetClientIdByKeyAsync` returning UUID or null; **do not include bootstrap seeding here — seeding belongs in Program.cs** per U1 fix)
- [X] T018 Implement `src/MeisterProPR.Infrastructure/Repositories/PostgresCrawlConfigurationRepository.cs` (implements `ICrawlConfigurationRepository`)
- [X] T019 Implement `src/MeisterProPR.Api/Middleware/AdminKeyMiddleware.cs` (reads `MEISTER_ADMIN_KEY` from `IConfiguration`; applied only to routes annotated with `[AdminAuthorize]` attribute or a dedicated policy; returns 401 if header missing or wrong; NEVER logs the key value)

### DI Wiring

- [X] T020 Update `src/MeisterProPR.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs` — register `MeisterProPRDbContext`, `PostgresJobRepository` (as `IJobRepository`), `PostgresClientRegistry` (as `IClientRegistry`), `PostgresCrawlConfigurationRepository`, `StubAssignedPrFetcher` in dev/stub mode
- [X] T021 Update `src/MeisterProPR.Api/Program.cs` — register `AdminKeyMiddleware`; call `await db.Database.MigrateAsync()` before `app.Run()`; after migration, run one-time client bootstrap: if `clients` table is empty and `MEISTER_CLIENT_KEYS` env var is set, insert each key as an active `Client` record and log a deprecation warning; extend Serilog destructuring policies to scrub `X-Admin-Key` and `DB_CONNECTION_STRING`; keep `InMemoryJobRepository` wired for `WebApplicationFactory` test overrides (U1 fix: bootstrap lives here, not in PostgresClientRegistry)

**Checkpoint**: `dotnet test` passes for domain + foundational tests. PostgreSQL repository stores and retrieves jobs correctly.

---

## Phase 3: User Story 1 — Trigger Automated Review via Reviewer Assignment (Priority: P1) 🎯 MVP

**Goal**: Developer adds service account as reviewer in ADO UI → backend detects within one crawl cycle → creates and executes a `ReviewJob` automatically.

**Independent Test**: Add the service account as reviewer on a real/test PR; within `crawlIntervalSeconds` the backend logs a new job created; the job transitions to Completed and ADO receives review comments.

### Tests for US1 ⚠️ Write first — confirm failing before T026

- [X] T022 [P] [TEST] [US1] Write failing unit tests in `tests/MeisterProPR.Application.Tests/Services/PrCrawlServiceTests.cs` using NSubstitute: (a) assigned PR with no active job → `Add` called once; (b) assigned PR with existing active (non-Failed) job → `Add` NOT called; (c) assigned PR with existing Failed job → `Add` called (retry); (d) multiple crawl configs → each is processed
- [X] T023 [P] [TEST] [US1] Write failing unit tests in `tests/MeisterProPR.Api.Tests/Workers/AdoPrCrawlerWorkerTests.cs`: `PrCrawlService.CrawlAsync` is called on each timer tick; cancellation token stops the worker; exceptions in `CrawlAsync` are caught and logged (worker does not crash)
- [X] T024 [P] [TEST] [US1] Write failing unit tests in `tests/MeisterProPR.Infrastructure.Tests/AzureDevOps/AdoAssignedPrFetcherTests.cs` using NSubstitute on `GitHttpClient`: `GetPullRequestsAsync` called with correct `ReviewerId` and `Status = Active`; `GetPullRequestIterationsAsync` called per PR; latest iteration ID extracted correctly; empty result list handled

### Implementation for US1

- [X] T025 [US1] Implement `src/MeisterProPR.Application/Services/PrCrawlService.cs`: call `ICrawlConfigurationRepository.GetAllActiveAsync` → for each config call `IAssignedPullRequestFetcher.GetAssignedOpenPullRequestsAsync` → for each PR ref call `IJobRepository.FindActiveJob` → if null (or only Failed), create `new ReviewJob { Id = Guid.NewGuid(), ClientKey = null, ... }` and call `IJobRepository.Add`; use `[LoggerMessage]` for structured log entries
- [X] T026 [US1] Implement `src/MeisterProPR.Infrastructure/AzureDevOps/AdoAssignedPrFetcher.cs`: get `GitHttpClient` via `VssConnectionFactory`; call `GetPullRequestsAsync(projectId, null, criteria)` with `criteria.ReviewerId = config.ReviewerId` and `criteria.Status = Active`; for each PR call `GetPullRequestIterationsAsync` and take `Max(i => i.Id)`; return `IReadOnlyList<AssignedPullRequestRef>`; wrap in `Activity` span
- [X] T027 [US1] Implement `src/MeisterProPR.Api/Workers/AdoPrCrawlerWorker.cs`: `BackgroundService` using `PeriodicTimer` with interval from `PR_CRAWL_INTERVAL_SECONDS` env var (default 60 s, minimum 10 s); call `PrCrawlService.CrawlAsync(ct)` on each tick; wrap in `Activity` span; catch all exceptions, log as error — never crash the host process (I1 fix: global env var used; `PeriodicTimer` is fixed-interval and does not support dynamic per-config intervals — per-config `crawlIntervalSeconds` is stored for future use but the global var governs the current worker cycle)
- [X] T028 [US1] Register `PrCrawlService` (scoped), `AdoAssignedPrFetcher` / `StubAssignedPrFetcher` (scoped, mode-selected), `AdoPrCrawlerWorker` (hosted service) in `src/MeisterProPR.Api/Program.cs`
- [X] T050 [US1] Handle EC-002 (PR abandoned while Processing) in `src/MeisterProPR.Application/Services/ReviewOrchestrationService.cs`: if `IPullRequestFetcher.FetchAsync` returns a PR with status other than Active, call `IJobRepository.SetFailed(jobId, "PR was closed or abandoned before review could begin")` instead of proceeding; add corresponding test case to `tests/MeisterProPR.Application.Tests/Services/ReviewOrchestrationServiceTests.cs` (C3 fix)

**Checkpoint**: With a configured crawl target in the DB, adding the service account as ADO reviewer triggers a new `ReviewJob` on next crawl cycle. Existing reviewed PRs are skipped.

---

## Phase 4: User Story 2 — View Job History and Status (Priority: P2)

**Goal**: Operator queries `GET /jobs` and sees all jobs (past + present) after any service restart. Admin can register clients; clients manage their own crawl configurations.

**Independent Test**: Create a job, restart the service, query `GET /jobs` with `X-Admin-Key` → job visible with correct pre-restart state.

### Tests for US2 ⚠️ Write first — confirm failing before T033

- [X] T029 [P] [TEST] [US2] Write failing integration tests in `tests/MeisterProPR.Api.Tests/Controllers/JobsControllerTests.cs` using `WebApplicationFactory`: `GET /jobs` returns `200` with valid `X-Admin-Key`; returns `401` without it; pagination (`limit` / `offset`) works; `?status=Completed` filters correctly; response items contain `clientId` (UUID or null) and NOT `clientKey` (S1 fix)
- [X] T030 [P] [TEST] [US2] Write failing integration tests for admin client management in `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerTests.cs`: `POST /clients` requires `X-Admin-Key`; returns `201` with correct body (key NOT in response); `409` on duplicate key; `GET /clients` returns list without keys; `PATCH /clients/{id}` toggles `isActive`
- [X] T031 [P] [TEST] [US2] Write failing integration tests for crawl configuration endpoints in `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerTests.cs`: `POST /clients/{id}/crawl-configurations` requires `X-Client-Key` that owns `clientId`; returns `403` for wrong client; returns `201` on success; `GET` and `PATCH` endpoints similarly ownership-checked
- [X] T048 [P] [TEST] [US2] Write performance smoke test in `tests/MeisterProPR.Api.Tests/Controllers/JobsControllerTests.cs`: seed 10,000 `ReviewJob` rows via `IJobRepository.GetAllJobsAsync`-compatible bulk insert; assert `GET /jobs?limit=100` responds in under 2 seconds (SC-004 / C2 fix); documents that `ix_review_jobs_status` index must be active

### Implementation for US2

- [X] T032 [US2] Implement `src/MeisterProPR.Api/Controllers/JobsController.cs` with `GET /jobs` (admin-only via `AdminKeyMiddleware`, pagination via `limit`/`offset` query params, optional `?status=` filter); response DTO uses `clientId` (UUID or null) — never `clientKey` raw string (S1 fix); calls `IJobRepository.GetAllJobsAsync`; full XML docs (`<summary>`, `<param>`, `<response>`)
- [X] T033 [US2] Implement `src/MeisterProPR.Api/Controllers/ClientsController.cs` with `POST/GET/PATCH /clients` (admin-protected) and `POST/GET/PATCH /clients/{clientId}/crawl-configurations` (client-key protected with ownership check — resolve caller's UUID via `IClientRegistry.GetClientIdByKeyAsync(X-Client-Key)` then assert it matches `clientId` route param; return `403` if mismatch); full XML docs; never echo the client `key` in responses (A1 fix)
- [X] T034 [US2] Extend `/healthz` to include a database connectivity check in `src/MeisterProPR.Api/HealthChecks/` (add a new `DatabaseHealthCheck : IHealthCheck` that executes `SELECT 1` via `MeisterProPRDbContext`; returns `Unhealthy` if DB unreachable)
- [X] T035 [US2] Add crawl observability: new Prometheus counter `meisterpropr_crawl_prs_discovered_total` and histogram `meisterpropr_crawl_duration_seconds` in `src/MeisterProPR.Api/Telemetry/ReviewJobMetrics.cs`; increment/record in `AdoPrCrawlerWorker`
- [X] T049 [US2] Add `IJobRepository.GetAllJobsAsync` implementation to `src/MeisterProPR.Infrastructure/Repositories/PostgresJobRepository.cs` and `GetProcessingJobsAsync`; ensure `PostgresJobRepository.TryTransition` sets `ProcessingStartedAt = DateTimeOffset.UtcNow` when transitioning to Processing (D1 fix: populate new column); add Activity spans wrapping `Add`, `TryTransition`, `SetResult`, `SetFailed` EF Core operations (L2 fix)

**Checkpoint**: `GET /jobs` returns all jobs across restarts. Admin can register clients. Clients manage their own crawl targets.

---

## Phase 5: User Story 3 — Prevent Duplicate Review Jobs Across Restarts (Priority: P3)

**Goal**: After a service restart, the backend reads persisted job state and does not create duplicate review jobs for already-processed PRs. Stale `Processing` jobs (from a crash) are recovered to `Pending`.

**Independent Test**: Create a `Completed` job; restart the service (new `WebApplicationFactory` instance); trigger a crawl cycle; confirm `GetAllJobs` count is unchanged (no new job for the same PR).

### Tests for US3 ⚠️ Write first — confirm failing before T038

- [X] T036 [P] [TEST] [US3] Write failing integration test for restart-idempotency in `tests/MeisterProPR.Api.Tests/PrCrawlRestartTests.cs`: seed a `Completed` job for PR #42 via `IJobRepository`; instantiate a fresh `WebApplicationFactory` (new DI scope, same Postgres via Testcontainers); call `PrCrawlService.CrawlAsync` with a stubbed `IAssignedPullRequestFetcher` returning PR #42; assert `IJobRepository.GetAllJobsAsync(limit: 100, offset: 0, status: null).total` is still 1 (A2 fix)
- [X] T037 [P] [TEST] [US3] Write failing unit test in `tests/MeisterProPR.Api.Tests/StartupRecoveryTests.cs`: given one `Processing` job in the database at startup, assert `Program.cs` bootstrap logic transitions it to `Pending` before the application starts accepting requests

### Implementation for US3

- [X] T038 [US3] Add startup recovery in `src/MeisterProPR.Api/Program.cs`: after `MigrateAsync()` (and client bootstrap), call `IJobRepository.GetProcessingJobsAsync()`; for each result call `TryTransition(Processing → Pending)`; log a warning per recovered job with PR reference (A3 fix)
- [X] T039 [US3] Verify `PostgresJobRepository.FindActiveJob` returns `null` for a `Failed` job (allowing retry) but the existing entity for `Pending`/`Processing`/`Completed` — add edge-case row to `PostgresJobRepositoryTests.cs` if the scenario is not already covered by T009

**Checkpoint**: Service restarts cleanly. No duplicate reviews. Stale `Processing` jobs are retried.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T040 [P] Regenerate `openapi.json` at repo root: `dotnet build src/MeisterProPR.Api` to trigger Swashbuckle generation, then commit updated file; verify new endpoints (`/clients`, `/clients/{id}/crawl-configurations`, `/jobs`) appear in the output
- [X] T041 [P] Update `CLAUDE.md` Key Environment Variables table to add `DB_CONNECTION_STRING`, `MEISTER_ADMIN_KEY`, `PR_CRAWL_INTERVAL_SECONDS`
- [X] T042 [P] Update `src/MeisterProPR.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs` description comment to reflect that `IJobRepository` is now backed by PostgreSQL; remove any stale references to `InMemoryJobRepository` from production DI (keep only in test helpers)
- [ ] T043 Run quickstart.md validation: `docker compose up` → seed client via `POST /clients` → add crawl config via `POST /clients/{id}/crawl-configurations` → verify `/healthz` returns `200` → verify `/metrics` returns Prometheus output → verify `GET /jobs` with admin key works

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Requires Phase 1 complete — BLOCKS all user stories
- **Phase 3 (US1)**: Requires Phase 2 complete
- **Phase 4 (US2)**: Requires Phase 2 complete (can run in parallel with Phase 3)
- **Phase 5 (US3)**: Requires Phase 3 and Phase 4 complete (depends on persistence + crawler both working)
- **Phase 6 (Polish)**: Requires all user story phases complete

### User Story Dependencies

- **US1 (P1)**: Foundational phase complete → can begin independently
- **US2 (P2)**: Foundational phase complete → can begin in parallel with US1
- **US3 (P3)**: Both US1 (crawler) and US2 (persistence/job-list) must be complete — it validates the combined behaviour

### Within Each Phase

1. `[TEST]` tasks written and confirmed **failing** before any implementation begins
2. Interface definitions (`T013`, `T014`) before implementations (`T016`–`T018`)
3. Infrastructure implementations before DI wiring (`T020`, `T021`)
4. Application service implementations before Api worker/controller (`T025` before `T027`)

---

## Parallel Execution Examples

### Phase 2 — Foundational (parallel opportunities)

```
Parallel group A — write all [TEST] tasks first:
  T009  PostgresJobRepository tests
  T010  PostgresClientRegistry tests
  T011  AdminKeyMiddleware tests
  T012  ReviewJob nullable ClientKey tests
  T013  IAssignedPullRequestFetcher interface
  T014  ICrawlConfigurationRepository interface
  T015  StubAssignedPrFetcher

Sequential after tests confirmed failing:
  T016  PostgresJobRepository implementation
  T017  PostgresClientRegistry implementation
  T018  PostgresCrawlConfigurationRepository implementation
  T019  AdminKeyMiddleware implementation
  T020  InfrastructureServiceExtensions update
  T021  Program.cs update
```

### Phase 3 — US1 (parallel opportunities)

```
Parallel group — write all [TEST] tasks first:
  T022  PrCrawlServiceTests
  T023  AdoPrCrawlerWorkerTests
  T024  AdoAssignedPrFetcherTests

Sequential after tests confirmed failing:
  T025  PrCrawlService implementation
  T026  AdoAssignedPrFetcher implementation
  T027  AdoPrCrawlerWorker implementation
  T028  DI registration
```

### Phase 4 — US2 (parallel opportunities)

```
Parallel group — write all [TEST] tasks first:
  T029  JobsControllerTests
  T030  ClientsController admin tests
  T031  ClientsController crawl config tests

Sequential after tests confirmed failing:
  T032  JobsController implementation
  T033  ClientsController implementation
  T034  DatabaseHealthCheck
  T035  Crawl metrics
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Phase 1: Setup (T001–T007)
2. Phase 2: Foundational (T008–T021)
3. Phase 3: US1 (T022–T028)
4. **STOP and VALIDATE**: Add service account as ADO reviewer → job appears → review posted
5. Deploy / demo

### Incremental Delivery

1. Setup + Foundational → postgres runs, job persistence works
2. US1 → crawler detects assignments and auto-reviews PRs
3. US2 → operators can view all jobs; admin manages clients and crawl targets
4. US3 → restart safety confirmed; stale jobs recovered
5. Polish → openapi.json committed, CLAUDE.md updated

---

## Task Summary

| Phase | Tasks | Test tasks | Parallel tasks | Fixes applied |
|-------|-------|-----------|----------------|---------------|
| Phase 1 — Setup | T001–T006 | 0 | 2 [P] | L1 (appsettings.json) |
| Phase 2 — Foundational | T007–T021, T044–T047 | 7 [TEST] | 12 [P] | C1, A1, A2, A3, U1 |
| Phase 3 — US1 | T022–T028, T050 | 3 [TEST] | 3 [P] | C3, I1 |
| Phase 4 — US2 | T029–T035, T048–T049 | 4 [TEST] | 4 [P] | S1, C2, D1, L2 |
| Phase 5 — US3 | T036–T039 | 2 [TEST] | 2 [P] | A2, A3 |
| Phase 6 — Polish | T040–T043 | 0 | 3 [P] | — |
| **Total** | **50 tasks** | **16 [TEST]** | **26 [P]** | **10 findings resolved** |
