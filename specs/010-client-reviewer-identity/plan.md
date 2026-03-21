# Implementation Plan: Client Self-Managed Reviewer Identity

**Branch**: `010-client-reviewer-identity` | **Date**: 2026-03-21 | **Spec**: [spec.md](spec.md)

## Summary

Allow a client to read its own profile and set its own reviewer identity using only its client key (`X-Client-Key`), without requiring admin intervention. Extends the existing `PUT /clients/{clientId}/reviewer-identity` endpoint to accept either admin or client credentials (with ownership check) and adds a new `GET /clients/{clientId}/profile` endpoint accessible by client key. No DB migrations or new Application/Infrastructure layers are required — the `reviewer_id` column and all service methods already exist.

## Technical Context

**Language/Version**: C# 13 / .NET 10, TFM `net10.0`
**Primary Dependencies**: ASP.NET Core MVC, EF Core 10.0.3, Npgsql 10.0.0, FluentValidation (all existing — no new packages)
**Storage**: PostgreSQL 17 via EF Core — `reviewer_id uuid` column already exists on `clients` table; no migrations needed
**Testing**: xUnit + NSubstitute; `WebApplicationFactory<Program>` for integration tests
**Target Platform**: Linux rootless container
**Project Type**: web-service (REST API)
**Performance Goals**: Response in < 3 s end-to-end (SC-001); consistent with existing endpoint latency
**Constraints**: Client may only access own record; admin access unchanged; no new NuGet packages
**Scale/Scope**: Single field update/read on an existing table; purely authorization-layer change

## Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. API-Contract-First | PASS | New endpoint + modified endpoint documented in `contracts/endpoints.md`; `openapi.json` must be regenerated and committed after implementation |
| II. Test-First | PASS | `[TEST]` tasks lead `tasks.md`; all scenarios from spec have corresponding acceptance tests |
| III. Container-First | PASS | No Windows APIs; no config changes; env-var auth model unchanged |
| IV. Clean Architecture | PASS | All changes are in `MeisterProPR.Api` only (controller + new response record); no layer violations |
| V. Security | PASS | Ownership check mirrors crawl-configuration pattern; client cannot access another client's record; sensitive fields (`HasAdoCredentials`, ADO secret) excluded from client-facing response |
| VI. Job Reliability | PASS | No job system changes |
| VII. Observability | PASS | Audit log via `[LoggerMessage]` partial method on `ClientsController`; structured fields: `clientId`, `actorType` (Admin vs. Client) |
| VIII. Code Style | PASS | `sealed` record, XML docs, primary constructor, `this.` qualification, no regional comments, Allman braces |

## Project Structure

### Documentation (this feature)

```text
specs/010-client-reviewer-identity/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── quickstart.md        ← Phase 1 output
├── contracts/
│   └── endpoints.md     ← Phase 1 output
└── tasks.md             ← Phase 2 output (/speckit.tasks)
```

### Source Code (affected files)

```text
src/
└── MeisterProPR.Api/
    └── Controllers/
        └── ClientsController.cs    ← new endpoint + auth extension + audit logging

tests/
├── MeisterProPR.Api.Tests/
│   └── Controllers/
│       └── ClientsControllerTests.cs   ← new + extended test cases
└── MeisterProPR.Application.Tests/
    └── (no changes required)
```

**Structure Decision**: All code changes are in `MeisterProPR.Api`. No new projects, no new files beyond the existing controller and its test class.

## Complexity Tracking

*No constitution violations — no entries required.*

## Implementation Design

### 1. New endpoint: `GET /clients/{clientId}/profile`

Add a new controller action `GetClientProfile` to `ClientsController`:

- Authenticate via `X-Client-Key` (same pattern as `GetCrawlConfigurations`)
- Resolve caller's client ID via `IClientRegistry.GetClientIdByKeyAsync`
- Enforce ownership: caller ID must equal `{clientId}`, else 403
- Fetch full DTO via `IClientAdminService.GetByIdAsync`
- Map to new `ClientProfileResponse` record (omits `HasAdoCredentials`)
- Return 200 with `ClientProfileResponse` or 404 if not found

**New response record** (in `ClientsController.cs`):
```csharp
public sealed record ClientProfileResponse(
    Guid Id,
    string DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAt,
    Guid? ReviewerId);
```

### 2. Modified endpoint: `PUT /clients/{clientId}/reviewer-identity`

Extend `PutReviewerIdentity` to accept either admin key or client key:

- First check if `X-Admin-Key` is valid (existing path — no change)
- Otherwise check `X-Client-Key`:
  - Resolve caller ID, enforce ownership (401 if no key, 403 if wrong client)
- Validate `SetReviewerIdentityRequest` via existing `SetReviewerIdentityRequestValidator`
- Call existing `IClientAdminService.SetReviewerIdentityAsync`
- Emit audit log entry distinguishing actor type

**Audit log** (partial class extension of `ClientsController`):
```csharp
[LoggerMessage(Level = LogLevel.Information,
    Message = "Reviewer identity updated for client {ClientId} by {ActorType}")]
private static partial void LogReviewerIdentityUpdated(
    ILogger logger, Guid clientId, string actorType);
```

### 3. No Application or Infrastructure changes

`IClientAdminService.GetByIdAsync` and `SetReviewerIdentityAsync` are already present and fully implemented. `IClientRegistry.GetClientIdByKeyAsync` is already used for ownership checks throughout the controller. No new interfaces, DTOs, or repository methods are needed.

### 4. Identity resolution — extension responsibility

Identity resolution (translating an ADO display name to a VSS identity GUID) is **not** handled by the backend. The `GET /identities/resolve` endpoint and its full implementation chain (`IIdentityResolver`, `AdoIdentityResolver`, `StubIdentityResolver`) were removed during implementation. The ADO Extension SDK, running within the settings extension browser context, performs this lookup directly using the identity picker typeahead API. See `research.md` Decision 6 for the full rationale.

### 5. `openapi.json` regeneration

After implementation, run the API locally (`dotnet run --project src/MeisterProPR.Api`) and re-export `openapi.json` (Swashbuckle endpoint: `GET /swagger/v1/swagger.json`). Commit the updated file.
