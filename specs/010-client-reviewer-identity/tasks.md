# Tasks: Client Self-Managed Reviewer Identity

**Input**: Design documents from `/specs/010-client-reviewer-identity/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/endpoints.md ✓

**Tests**: Included — TDD is mandatory per Constitution Principle II.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different test cases / no task dependency)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

---

## Phase 1: Setup

No new project structure or files required. All changes are within existing files:
- `src/MeisterProPR.Api/Controllers/ClientsController.cs`
- `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerReviewerTests.cs`

---

## Phase 2: Foundational

No new foundational infrastructure is required. The `reviewer_id` column, service methods, and ownership-check plumbing all exist. Proceed directly to user stories.

**Checkpoint**: Foundation is complete — user story implementation can begin.

---

## Phase 3: User Story 1 — Client Sets Its Own Reviewer Identity (Priority: P1) 🎯 MVP

**Goal**: A client authenticated with `X-Client-Key` can set its own reviewer identity using `PUT /clients/{clientId}/reviewer-identity`, without requiring an admin key.

**Independent Test**: Send `PUT /clients/{clientId}/reviewer-identity` with `X-Client-Key` header and a valid non-empty GUID. Expect 204. Then fetch via admin `GET /clients/{clientId}` and confirm `reviewerId` is stored.

### Tests for User Story 1 ⚠️ Write first — confirm FAILING before implementing

> All tests below currently return 401 (endpoint rejects non-admin callers). They must fail before T005.

- [x] T001 [US1] Add failing test `PutReviewerIdentity_WithClientKey_OwnClient_Returns204` in `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerReviewerTests.cs` — authenticate with `X-Client-Key` for own `clientId`, send valid non-empty GUID, assert 204 and persisted value
- [x] T002 [P] [US1] Add failing test `PutReviewerIdentity_WithClientKey_WrongClient_Returns403` in `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerReviewerTests.cs` — authenticate with `X-Client-Key` but target a different `clientId`, assert 403
- [x] T003 [P] [US1] Add failing test `PutReviewerIdentity_WithClientKey_EmptyGuid_Returns400` in `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerReviewerTests.cs` — authenticate with `X-Client-Key` for own client, send `Guid.Empty`, assert 400
- [x] T004 [P] [US1] Add failing test `PutReviewerIdentity_WithClientKey_IdempotentSameValue_Returns204` in `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerReviewerTests.cs` — set the same `reviewerId` twice with `X-Client-Key`, assert both calls return 204

### Implementation for User Story 1

- [x] T005 [US1] In `src/MeisterProPR.Api/Controllers/ClientsController.cs`: add `ILogger<ClientsController>` to the primary constructor parameters; add `partial` modifier to the class declaration; add `[LoggerMessage]`-attributed static partial method `LogReviewerIdentityUpdated(ILogger logger, Guid clientId, string actorType)` at `LogLevel.Information` with message `"Reviewer identity updated for client {ClientId} by {ActorType}"`
- [x] T006 [US1] In `src/MeisterProPR.Api/Controllers/ClientsController.cs`: refactor `PutReviewerIdentity` action to accept either `X-Admin-Key` or `X-Client-Key` — keep the existing admin path unchanged; add an else-if branch that resolves the caller's client ID via `IClientRegistry.GetClientIdByKeyAsync`, returns 401 if no valid key resolved, returns 403 if caller ID does not match `{clientId}`, then proceeds to validation and `IClientAdminService.SetReviewerIdentityAsync`; call `LogReviewerIdentityUpdated` with actor type `"Admin"` or `"Client"` after a successful update

**Checkpoint**: Run `dotnet test --filter "ClientsControllerReviewerTests"` — T001–T004 must now be green. Existing admin-path tests must remain green.

---

## Phase 4: User Story 2 — Client Reads Its Own Reviewer Identity (Priority: P2)

**Goal**: A client authenticated with `X-Client-Key` can retrieve its own profile (including `reviewerId`) via a new `GET /clients/{clientId}/profile` endpoint, without requiring an admin key.

**Independent Test**: Send `GET /clients/{clientId}/profile` with `X-Client-Key`. Expect 200 with JSON body containing `id`, `displayName`, `isActive`, `createdAt`, `reviewerId` (null if not yet set). Does not expose `hasAdoCredentials`.

### Tests for User Story 2 ⚠️ Write first — confirm FAILING before implementing

> All tests below currently return 404 (endpoint does not exist yet). They must fail before T011.

- [x] T007 [US2] Add failing test `GetClientProfile_WithClientKey_NoReviewerSet_Returns200WithNullReviewerId` in `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerReviewerTests.cs` — authenticate with `X-Client-Key` for own `clientId`, send `GET /clients/{clientId}/profile`, assert 200 and `reviewerId` is JSON null; also assert `hasAdoCredentials` is NOT present in the response
- [x] T008 [P] [US2] Add failing test `GetClientProfile_WithClientKey_AfterReviewerSet_Returns200WithReviewerId` in `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerReviewerTests.cs` — seed a client with `ReviewerId` pre-set; authenticate with matching `X-Client-Key` (use a second factory or seed approach); assert 200 and `reviewerId` matches the seeded value
- [x] T009 [P] [US2] Add failing test `GetClientProfile_WithClientKey_WrongClient_Returns403` in `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerReviewerTests.cs` — authenticate with `X-Client-Key` but target a different `clientId`, assert 403
- [x] T010 [P] [US2] Add failing test `GetClientProfile_WithoutClientKey_Returns401` in `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerReviewerTests.cs` — send `GET /clients/{clientId}/profile` with no key header, assert 401

### Implementation for User Story 2

- [x] T011 [US2] In `src/MeisterProPR.Api/Controllers/ClientsController.cs`: add a new `sealed record ClientProfileResponse(Guid Id, string DisplayName, bool IsActive, DateTimeOffset CreatedAt, Guid? ReviewerId)`; add a new `GetClientProfile` action mapped to `GET /clients/{clientId:guid}/profile` with full XML doc (`<summary>`, `<param>`, `<response>` for 200/401/403/404); authenticate via `X-Client-Key` with ownership check matching the pattern in `GetCrawlConfigurations`; call `IClientAdminService.GetByIdAsync`; return 200 with `ClientProfileResponse` or 404 if not found

**Checkpoint**: Run `dotnet test --filter "ClientsControllerReviewerTests"` — T007–T010 must now be green alongside all previous tests.

---

## Phase 5: User Story 3 — Admin Retains Reviewer Identity Management (Priority: P3)

**Goal**: Confirm that modifying `PutReviewerIdentity` for US1 has not broken any existing admin-path behaviour.

**Independent Test**: Run the full pre-existing test suite for `ClientsControllerReviewerTests`. All five existing tests must still pass unchanged.

### Regression verification for User Story 3

- [x] T012 [US3] Run `dotnet test --filter "ClientsControllerReviewerTests"` and confirm the following pre-existing tests all remain green: `PutReviewerIdentity_ValidGuid_Returns204AndPersists`, `PutReviewerIdentity_EmptyGuid_Returns400`, `PutReviewerIdentity_UnknownClient_Returns404`, `PutReviewerIdentity_WithoutAdminKey_Returns401`, `GetClient_AfterReviewerSet_ReviewerIdIsReturned`, `GetClient_BeforeReviewerSet_ReviewerIdIsNull` — no code changes expected; fix any regressions in the US1 implementation if any test fails

**Checkpoint**: Complete test suite passes — all three user stories independently verified.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T013 Run `dotnet run --project src/MeisterProPR.Api` locally and export the updated OpenAPI spec from `GET /swagger/v1/swagger.json`; overwrite `openapi.json` at the repository root with the new content and commit it alongside the implementation changes
- [x] T014 [P] Run `dotnet test` (full suite) to confirm no regressions beyond the reviewer controller tests; fix any unexpected failures
- [x] T015 [P] Run `dotnet format --verify-no-changes` and resolve any formatting drift in modified files (`ClientsController.cs`, test file)

---

## Phase 7: Post-Implementation — Identity Resolution Removal

**Decision**: The `GET /identities/resolve` backend endpoint and its full implementation chain were removed after it proved unable to provide fuzzy/partial matching for service principal identities via any of the ADO identity APIs (`_apis/identities`, `_apis/identitypicker/identities`). Identity resolution is now the responsibility of the Azure DevOps settings extension, using the ADO Extension SDK identity picker which has direct browser-context access to ADO's typeahead APIs. See `research.md` Decision 6.

- [x] T016 Remove `src/MeisterProPR.Api/Controllers/IdentitiesController.cs`
- [x] T017 [P] Remove `src/MeisterProPR.Application/Interfaces/IIdentityResolver.cs`
- [x] T018 [P] Remove `src/MeisterProPR.Infrastructure/AzureDevOps/AdoIdentityResolver.cs`
- [x] T019 [P] Remove `src/MeisterProPR.Infrastructure/AzureDevOps/StubIdentityResolver.cs`
- [x] T020 [P] Remove `tests/MeisterProPR.Infrastructure.Tests/AzureDevOps/AdoIdentityResolverTests.cs`
- [x] T021 Remove `IIdentityResolver` stub from `tests/MeisterProPR.Api.Tests/Controllers/ClientsControllerTests.cs` factory
- [x] T022 Remove `IIdentityResolver` and `AddHttpClient("AdoIdentity")` registrations from `InfrastructureServiceExtensions.cs` (both ADO and stub branches)
- [x] T023 Remove Identities folder and Resolve Identity request from `insomnia/clients.json`
- [x] T024 Update spec documents (`spec.md`, `research.md`, `contracts/endpoints.md`, `quickstart.md`, `plan.md`, `tasks.md`) to reflect extension-driven identity resolution

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — no action needed
- **Phase 2 (Foundational)**: No action needed — proceed immediately to Phase 3
- **Phase 3 (US1)**: No prior phase dependencies; must complete before Phase 5
- **Phase 4 (US2)**: No dependency on Phase 3 — can start in parallel with Phase 3 (different test methods and a new action, no code conflict)
- **Phase 5 (US3)**: Depends on Phase 3 being complete (verifies US1 implementation regression)
- **Phase 6 (Polish)**: Depends on Phases 3, 4, and 5 being complete

### User Story Dependencies

- **User Story 1 (P1)**: Independent — starts immediately
- **User Story 2 (P2)**: Independent — can start in parallel with US1 (disjoint test methods; new action in same file, no conflict with US1 changes)
- **User Story 3 (P3)**: Depends on US1 being complete (regression check)

### Within Each User Story

- [TEST] tasks MUST be written and confirmed FAILING before implementation begins
- Tests before implementation within each story
- T005 before T006 (logger setup before auth refactor)

### Parallel Opportunities

- T002, T003, T004 can be written in parallel with T001 (independent test methods)
- T007, T008, T009, T010 can be written in parallel (independent test methods)
- US1 tests (T001–T004) can be written in parallel with US2 tests (T007–T010)
- T014 and T015 can run in parallel in Phase 6

---

## Parallel Example: User Story 1 Tests

```text
# Write all failing tests for US1 together (parallel):
T001: PutReviewerIdentity_WithClientKey_OwnClient_Returns204
T002: PutReviewerIdentity_WithClientKey_WrongClient_Returns403
T003: PutReviewerIdentity_WithClientKey_EmptyGuid_Returns400
T004: PutReviewerIdentity_WithClientKey_IdempotentSameValue_Returns204

# Then implement (sequential):
T005: Add logger + [LoggerMessage] partial method
T006: Extend PutReviewerIdentity with dual-auth
```

## Parallel Example: User Story 2 Tests

```text
# Write all failing tests for US2 together (parallel):
T007: GetClientProfile_WithClientKey_NoReviewerSet_Returns200WithNullReviewerId
T008: GetClientProfile_WithClientKey_AfterReviewerSet_Returns200WithReviewerId
T009: GetClientProfile_WithClientKey_WrongClient_Returns403
T010: GetClientProfile_WithoutClientKey_Returns401

# Then implement (single task — all tests drive one new action):
T011: Add ClientProfileResponse record + GetClientProfile action
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Write and confirm T001–T004 failing
2. Implement T005–T006
3. Run checkpoint: T001–T004 green, existing tests green
4. **STOP and VALIDATE** — `PUT reviewer-identity` now works with client key

### Incremental Delivery

1. **MVP**: US1 (T001–T006) — client can self-service write reviewer identity
2. **Read capability**: US2 (T007–T011) — client can read own profile with reviewer identity
3. **Regression gate**: US3 (T012) — admin path confirmed intact
4. **Finalize**: Polish (T013–T015) — openapi.json, full suite, formatting

### Single Developer Sequence

T001 → T002 → T003 → T004 → (confirm all fail) → T005 → T006 → (confirm T001–T004 green) → T007 → T008 → T009 → T010 → (confirm all fail) → T011 → (confirm T007–T010 green) → T012 → T013 → T014 → T015

---

## Notes

- [P] tasks = independent test methods or non-conflicting files — safe to write simultaneously
- [US?] label maps task to specific user story for traceability
- Each user story is independently completable and testable at its checkpoint
- **Always confirm tests FAIL before implementing the corresponding feature code**
- `dotnet format --verify-no-changes` runs in CI — fix formatting before committing
- The `ClientsController` must be made `partial` to support `[LoggerMessage]`; `sealed partial` is valid C#
- No EF Core migrations, no new NuGet packages, no Application/Infrastructure changes
