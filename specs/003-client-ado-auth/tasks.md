# Tasks: Per-Client ADO Identity

**Input**: Design documents from `/specs/003-client-ado-auth/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/ ✓, quickstart.md ✓

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on each other)
- **[Story]**: Which user story this task belongs to ([US1], [US2], [US3])
- **[TEST]**: Write test FIRST — confirm it FAILS before writing implementation

## Path Conventions

- **API layer**: `src/MeisterProPR.Api/`
- **Application layer**: `src/MeisterProPR.Application/`
- **Infrastructure layer**: `src/MeisterProPR.Infrastructure/`
- **Api tests**: `tests/MeisterProPR.Api.Tests/`
- **Infrastructure tests**: `tests/MeisterProPR.Infrastructure.Tests/`

---

## Phase 1: Setup

No new NuGet packages, projects, or frameworks required. All dependencies (`Azure.Identity`, `EF Core`, `Npgsql`) are already referenced. Proceed directly to Foundational phase.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Application-layer contracts, DB model changes, migration, repository implementations, and factory refactoring that ALL user stories depend on. No user story work can begin until this phase is complete.

**⚠️ CRITICAL**: The `IIdentityResolver` interface signature change (T006) is a compile-breaking change. T007 and T008 must follow T006 immediately to restore compilation before any test can run.

### Tests for Foundational Infrastructure ⚠️

> **NOTE: Write these tests FIRST so they exist as failing stubs before implementation begins**

- [ ] T001 [P] [TEST] Write failing test class `NullClientAdoCredentialRepositoryTests` asserting `GetByClientIdAsync` always returns null and `UpsertAsync`/`ClearAsync` are no-ops in `tests/MeisterProPR.Infrastructure.Tests/Repositories/NullClientAdoCredentialRepositoryTests.cs`
- [ ] T002 [P] [TEST] Write failing test class `PostgresClientAdoCredentialRepositoryTests` with tests for `UpsertAsync` (creates row), `GetByClientIdAsync` (returns stored credentials), `UpsertAsync` again (updates existing row, not duplicate insert), and `ClearAsync` (nulls all three columns) in `tests/MeisterProPR.Infrastructure.Tests/Repositories/PostgresClientAdoCredentialRepositoryTests.cs`
- [ ] T003 [P] [TEST] Write failing tests for `VssConnectionFactory.GetConnectionAsync` with `ClientAdoCredentials?` parameter: (a) non-null credentials builds `ClientSecretCredential` path; (b) null credentials falls back to global `TokenCredential`; (c) cache key distinguishes `orgUrl::clientId` from `orgUrl::global` in `tests/MeisterProPR.Infrastructure.Tests/AzureDevOps/VssConnectionFactoryTests.cs`

### Application Layer Changes

- [ ] T004 [P] Create `ClientAdoCredentials` sealed record with properties `TenantId`, `ClientId`, `Secret` (all `string`, no Azure dependencies) in `src/MeisterProPR.Application/DTOs/ClientAdoCredentials.cs`
- [ ] T005 [P] Create `IClientAdoCredentialRepository` interface with three methods — `Task<ClientAdoCredentials?> GetByClientIdAsync(Guid clientId, CancellationToken ct)`, `Task UpsertAsync(Guid clientId, ClientAdoCredentials credentials, CancellationToken ct)`, `Task ClearAsync(Guid clientId, CancellationToken ct)` — in `src/MeisterProPR.Application/Interfaces/IClientAdoCredentialRepository.cs`
- [ ] T006 Update `IIdentityResolver.ResolveAsync` signature to add `Guid clientId` as the third parameter (before `CancellationToken`) in `src/MeisterProPR.Application/Interfaces/IIdentityResolver.cs`
- [ ] T007 Update `StubIdentityResolver.ResolveAsync` to match the new 4-parameter signature (add ignored `Guid clientId` parameter) in `src/MeisterProPR.Infrastructure/AzureDevOps/StubIdentityResolver.cs`
- [ ] T008 Fix all existing `IIdentityResolver` mock setups and usages in test projects to use the new 4-parameter signature — search for `ResolveAsync` in `tests/` and update every call site in `tests/MeisterProPR.Api.Tests/`

### DB Model and Migration

- [ ] T009 Add three nullable `string?` auto-properties to `ClientRecord`: `AdoTenantId`, `AdoClientId`, `AdoClientSecret` in `src/MeisterProPR.Infrastructure/Data/Models/ClientRecord.cs`
- [ ] T010 Map the three new nullable properties to columns `ado_tenant_id`, `ado_client_id`, `ado_client_secret` (all `text`, nullable) in `src/MeisterProPR.Infrastructure/Data/Configurations/ClientEntityTypeConfiguration.cs`
- [ ] T011 Generate EF Core migration by running `dotnet ef migrations add AddClientAdoCredentials --project src/MeisterProPR.Infrastructure --startup-project src/MeisterProPR.Api` and verify the generated `Up`/`Down` methods add/drop the three columns on the `clients` table

### Infrastructure Implementations

- [ ] T012 [P] Create `NullClientAdoCredentialRepository` implementing `IClientAdoCredentialRepository`: `GetByClientIdAsync` returns `null`; `UpsertAsync` and `ClearAsync` are no-ops in `src/MeisterProPR.Infrastructure/Repositories/NullClientAdoCredentialRepository.cs`
- [ ] T013 Create `PostgresClientAdoCredentialRepository` implementing `IClientAdoCredentialRepository` via EF Core `MeisterProPRDbContext`: `GetByClientIdAsync` reads the three columns from `ClientRecord` and returns a `ClientAdoCredentials` if all three non-null; `UpsertAsync` finds the record by primary key and sets all three columns; `ClearAsync` nulls all three columns — all changes via `SaveChangesAsync` in `src/MeisterProPR.Infrastructure/Repositories/PostgresClientAdoCredentialRepository.cs`
- [ ] T014 Update `VssConnectionFactory`: change `GetConnectionAsync` signature to accept `ClientAdoCredentials? credentials` as second parameter; resolve `ClientSecretCredential(credentials.TenantId, credentials.ClientId, credentials.Secret)` when `credentials != null`, else use the existing global `TokenCredential`; change the cache key from `orgUrl` to `$"{orgUrl}::{credentials?.ClientId ?? "global"}"` in `src/MeisterProPR.Infrastructure/AzureDevOps/VssConnectionFactory.cs`
- [ ] T015 Update `AdoIdentityResolver` to inject `IClientAdoCredentialRepository`; in `ResolveAsync` call `GetByClientIdAsync(clientId, ct)`, convert the returned `ClientAdoCredentials?` to a `TokenCredential` (`ClientSecretCredential` when non-null, global credential when null), and use that credential to obtain the Bearer token in `src/MeisterProPR.Infrastructure/AzureDevOps/AdoIdentityResolver.cs`

### DI Registration and Observability

- [ ] T016 Register `IClientAdoCredentialRepository`: in DB mode register `PostgresClientAdoCredentialRepository` (scoped); in legacy mode register `NullClientAdoCredentialRepository` (singleton) in `src/MeisterProPR.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`
- [ ] T017 Add `"AdoClientSecret"` to the Serilog destructuring-policy scrub list alongside the existing `X-Client-Key`, `X-Ado-Token`, and `AZURE_CLIENT_SECRET` entries in `src/MeisterProPR.Api/Program.cs`

**Checkpoint**: Foundation is complete. All three new repository implementations exist, the factory accepts per-call credentials, and the identity resolver is credential-aware. User story phases can now begin.

---

## Phase 3: User Story 1 — Register a Client with Its Own ADO Identity (Priority: P1) 🎯 MVP

**Goal**: Admin attaches ADO service principal credentials to a client via `PUT /clients/{id}/ado-credentials`; all subsequent ADO operations (crawl, fetch, comment, identity resolve) for that client's projects use the client's credentials automatically.

**Independent Test**: Register a client, call `PUT /clients/{id}/ado-credentials`, then call `POST /clients/{id}/crawl-configurations` with a reviewer display name — the identity resolution must succeed using the client's service principal. Verify `GET /clients/{id}` shows `hasAdoCredentials: true`.

### Tests for User Story 1 ⚠️

> **Write these FIRST — confirm they FAIL before implementation**

- [ ] T018 [P] [US1] [TEST] Write failing integration test for `PUT /clients/{clientId}/ado-credentials` returning `204 No Content` when all three fields supplied, `400` when any field is missing or blank, `401` when no `X-Admin-Key`, and `404` when client does not exist in `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerTests.cs`
- [ ] T019 [P] [US1] [TEST] Write failing unit test for `AdoAssignedPrFetcher`: given `IClientAdoCredentialRepository` returns `ClientAdoCredentials`, `GetConnectionAsync` is called with those credentials (not null); given repository returns null, `GetConnectionAsync` is called with null in `tests/MeisterProPR.Infrastructure.Tests/AzureDevOps/AdoAssignedPrFetcherTests.cs`
- [ ] T020 [P] [US1] [TEST] Write failing unit test for `AdoIdentityResolver.ResolveAsync`: given `IClientAdoCredentialRepository` returns credentials for `clientId`, the outgoing Bearer token is obtained from a `ClientSecretCredential` (not the global credential) in `tests/MeisterProPR.Infrastructure.Tests/AzureDevOps/AdoIdentityResolverTests.cs`

### Implementation for User Story 1

- [ ] T021 [US1] Update `ClientResponse` sealed record to add `bool HasAdoCredentials`, `string? AdoTenantId`, `string? AdoClientId` — no Secret field must be present in `src/MeisterProPR.Api/Controllers/ClientsController.cs`
- [ ] T022 [US1] Add `SetAdoCredentialsRequest` sealed record with `string TenantId`, `string ClientId`, `string Secret` (all required, no nullability) in `src/MeisterProPR.Api/Controllers/ClientsController.cs`
- [ ] T023 [US1] Add `PutAdoCredentials` action: `[HttpPut("clients/{clientId:guid}/ado-credentials")]`, `X-Admin-Key` guard, validate all three fields are non-blank (return `400` if any missing), verify client exists (return `404` if not), call `IClientAdoCredentialRepository.UpsertAsync`, return `204 No Content` — include full XML doc comments in `src/MeisterProPR.Api/Controllers/ClientsController.cs`
- [ ] T024 [US1] Update all existing `ClientResponse(...)` construction sites in `CreateClient`, `GetClient`, `GetClients`, and `PatchClient` to populate `HasAdoCredentials`, `AdoTenantId`, `AdoClientId` from the loaded `ClientRecord` in `src/MeisterProPR.Api/Controllers/ClientsController.cs`
- [ ] T025 [US1] Update `ClientsController.AddCrawlConfiguration` to pass `clientId` as the third argument to `identityResolver.ResolveAsync(request.OrganizationUrl, request.ReviewerDisplayName, clientId, ct)` in `src/MeisterProPR.Api/Controllers/ClientsController.cs`
- [ ] T026 [US1] Inject `IClientAdoCredentialRepository` into `AdoAssignedPrFetcher`; before calling `connectionFactory.GetConnectionAsync(config.OrganizationUrl, ...)`, call `await credentialRepo.GetByClientIdAsync(config.ClientId, ct)` and pass the result as the second argument in `src/MeisterProPR.Infrastructure/AzureDevOps/AdoAssignedPrFetcher.cs`
- [ ] T027 [P] [US1] Inject `IClientAdoCredentialRepository` into `AdoPullRequestFetcher`; resolve per-client credentials before each `GetConnectionAsync` call using `config.ClientId` in `src/MeisterProPR.Infrastructure/AzureDevOps/AdoPullRequestFetcher.cs`
- [ ] T028 [P] [US1] Inject `IClientAdoCredentialRepository` into `AdoCommentPoster`; resolve per-client credentials before each `GetConnectionAsync` call using `config.ClientId` in `src/MeisterProPR.Infrastructure/AzureDevOps/AdoCommentPoster.cs`

**Checkpoint**: User Story 1 is fully functional. Admin can attach credentials to a client; all PR crawl and review operations for that client authenticate using the client's service principal.

---

## Phase 4: User Story 2 — Rotate or Remove ADO Credentials (Priority: P2)

**Goal**: Admin can replace (rotate) or delete (clear) credentials via `PUT` and `DELETE /clients/{id}/ado-credentials`. The next job dispatched after the change uses the updated credentials.

**Independent Test**: PUT credentials, then PUT again with a different secret — confirm the second secret is stored (no duplicate row, previous overwritten). Then DELETE — confirm `hasAdoCredentials` becomes `false` on GET.

### Tests for User Story 2 ⚠️

> **Write these FIRST — confirm they FAIL before implementation**

- [ ] T029 [P] [US2] [TEST] Write failing integration test for `DELETE /clients/{clientId}/ado-credentials`: given a client with credentials, returns `204 No Content` and subsequent `GET /clients/{id}` shows `hasAdoCredentials: false`; given a client with no credentials, still returns `204 No Content` (idempotent); given missing client, returns `404` in `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerTests.cs`
- [ ] T030 [P] [US2] [TEST] Write failing integration test for credential rotation: `PUT` credentials, then `PUT` again with new secret, then `GET /clients/{id}` — verify only one credential set stored (no duplicate insert); also verify `PostgresClientAdoCredentialRepository.UpsertAsync` called twice on same `clientId` does not throw or create duplicate records in `tests/MeisterProPR.Infrastructure.Tests/Repositories/PostgresClientAdoCredentialRepositoryTests.cs`

### Implementation for User Story 2

- [ ] T031 [US2] Add `DeleteAdoCredentials` action: `[HttpDelete("clients/{clientId:guid}/ado-credentials")]`, `X-Admin-Key` guard, verify client exists (return `404` if not), call `IClientAdoCredentialRepository.ClearAsync`, return `204 No Content` — include full XML doc comments in `src/MeisterProPR.Api/Controllers/ClientsController.cs`

**Checkpoint**: User Stories 1 and 2 are both functional. Full credential lifecycle (set, rotate, clear) works via the admin API.

---

## Phase 5: User Story 3 — Inspect a Client Without Exposing Secrets (Priority: P3)

**Goal**: `GET /clients/{id}` and `GET /clients` show `hasAdoCredentials`, `adoTenantId`, `adoClientId` — never the secret.

**Independent Test**: Register a client, PUT credentials, then GET the client — response body must not contain any field named `secret`, `adoClientSecret`, or similar; `hasAdoCredentials` must be `true`; `adoTenantId` and `adoClientId` must match what was set.

### Tests for User Story 3 ⚠️

> **Write these FIRST — confirm they FAIL before implementation**

- [ ] T032 [P] [US3] [TEST] Write failing integration test confirming `GET /clients/{clientId}` response JSON does not contain any key matching `secret` (case-insensitive), correctly returns `hasAdoCredentials: true`, `adoTenantId`, and `adoClientId` when credentials are stored, and `hasAdoCredentials: false` with null fields when no credentials stored in `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerTests.cs`
- [ ] T033 [P] [US3] [TEST] Write failing integration test confirming `GET /clients` list response — each item must not contain any secret-related key, and items for clients with credentials correctly show `hasAdoCredentials: true` in `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerTests.cs`

### Implementation for User Story 3

- [ ] T034 [US3] Verify `ClientResponse` sealed record definition contains no `Secret`, `AdoClientSecret`, or analogous property (compile-time enforcement); confirm `CreateClient`, `GetClient`, `GetClients`, `PatchClient` all return `ClientResponse` — no ad-hoc response type that might inadvertently include a secret field in `src/MeisterProPR.Api/Controllers/ClientsController.cs`

**Checkpoint**: All three user stories are independently functional and verified. Credential lifecycle is complete; secret is structurally absent from all API responses.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T035 Run `dotnet test` from repository root and confirm all tests pass (145 existing + new tests green)
- [ ] T036 [P] Regenerate `openapi.json` (run the app with Swashbuckle export or `dotnet run -- --swagger-export` equivalent) and commit the updated file to the repository root to satisfy the API-Contract-First principle
- [ ] T037 Manually validate the quickstart.md flow against a running backend with a real or stub PostgreSQL instance: register client, PUT credentials, POST crawl-config, confirm identity resolution uses client credentials, DELETE credentials, confirm fallback to global

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 2 (Foundational)**: No dependencies — start immediately
- **Phase 3 (US1)**: Depends on Phase 2 completion (T004–T017 must all be done)
- **Phase 4 (US2)**: Depends on Phase 2 completion; `DELETE` endpoint builds on `IClientAdoCredentialRepository.ClearAsync` which is in Phase 2
- **Phase 5 (US3)**: Depends on Phase 3 completion (ClientResponse shape defined in US1 tasks T021/T024)
- **Phase 6 (Polish)**: Depends on all story phases complete

### User Story Dependencies

- **US1 (P1)**: Starts after Foundational complete — no story dependencies
- **US2 (P2)**: Starts after Foundational complete — `ClearAsync` is in Foundational; only new work is the DELETE endpoint
- **US3 (P3)**: Starts after US1 complete — verifies response shape set in US1

### Within Each Phase

- [TEST] tasks written first and confirmed failing
- Application-layer types (T004, T005) before Infrastructure implementations (T012, T013)
- Interface signature change (T006) followed immediately by compilation fixes (T007, T008)
- Migration generated after model + config changes (T009, T010 → T011)
- DI registration (T016) after implementations exist (T012, T013)

### Parallel Opportunities

- Within Phase 2: T001, T002, T003 (test stubs) in parallel; T004 + T005 in parallel; T012 + T013 after T004/T005
- Within Phase 3: T018, T019, T020 (tests) in parallel; T027 + T028 in parallel (different files, same pattern as T026)
- Within Phase 4: T029 + T030 (tests) in parallel
- Within Phase 5: T032 + T033 (tests) in parallel
- US2 and US3 can begin in parallel once Phase 2 + US1 are done (US3 needs T021/T024 from US1)

---

## Parallel Example: Phase 2 Foundation

```text
Round 1 — Write failing tests in parallel:
  T001: NullClientAdoCredentialRepositoryTests.cs
  T002: PostgresClientAdoCredentialRepositoryTests.cs
  T003: VssConnectionFactoryTests.cs (per-client credential path)

Round 2 — Create Application contracts in parallel:
  T004: ClientAdoCredentials DTO
  T005: IClientAdoCredentialRepository interface
  (T006 is sequential — must happen after T005)

Round 3 — Infrastructure implementations in parallel:
  T012: NullClientAdoCredentialRepository
  T014: VssConnectionFactory update
  (T013 depends on T009, T010, T011 — sequential)
```

## Parallel Example: Phase 3 (US1)

```text
Round 1 — Write failing tests in parallel:
  T018: PUT endpoint tests
  T019: AdoAssignedPrFetcher credential tests
  T020: AdoIdentityResolver credential tests

Round 2 — API layer changes (T021–T025 are sequential; same file):
  T021 → T022 → T023 → T024 → T025 (all ClientsController.cs — sequential)

Round 3 — ADO service changes in parallel:
  T026: AdoAssignedPrFetcher
  T027: AdoPullRequestFetcher  [P]
  T028: AdoCommentPoster        [P]
```

---

## Implementation Strategy

### MVP (User Story 1 Only)

1. Complete Phase 2: Foundational (T001–T017)
2. Complete Phase 3: User Story 1 (T018–T028)
3. **STOP and VALIDATE**: `PUT /clients/{id}/ado-credentials` works; ADO crawl uses client credentials
4. Run all tests — confirm passing

### Incremental Delivery

1. Setup + Foundational → infra ready
2. US1 → credential registration + ADO usage → **demo: multi-tenant identity works**
3. US2 → rotation + removal → **demo: full credential lifecycle**
4. US3 → observability (GET response shape confirmed) → feature complete
5. Polish → `openapi.json` committed, quickstart validated

---

## Notes

- [P] tasks operate on different files and have no mutual dependency
- [TEST] tasks must be written FIRST and confirmed failing (compile error or runtime failure) before the corresponding implementation task begins
- `IIdentityResolver` signature change (T006) is a breaking compile change — fix T007 and T008 immediately after
- `AdoClientSecret` must never appear in any JSON response; the `ClientResponse` sealed record is the compile-time enforcement mechanism
- Commit after each logical group (e.g., after all Foundational tasks green, after US1 green)
