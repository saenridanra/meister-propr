# Research: Client Self-Managed Reviewer Identity

**Feature**: 010-client-reviewer-identity
**Date**: 2026-03-21

---

## Decision 1: Authorization model for `PUT /clients/{clientId}/reviewer-identity`

**Decision**: Extend the existing endpoint to accept either `X-Admin-Key` or `X-Client-Key` (with ownership check), rather than adding a parallel endpoint.

**Rationale**: The `PUT /clients/{clientId}/crawl-configurations` family already demonstrates the ownership-check pattern (resolve caller ID from key, compare to `{clientId}` path parameter, return 403 on mismatch). Reusing this pattern keeps the surface area minimal and the URL scheme stable. Adding a duplicate URL would fragment the contract and require coordinated extension updates.

**Alternatives considered**:
- Separate client-accessible URL (e.g. `PUT /clients/{clientId}/self/reviewer-identity`) — rejected because it duplicates state management at a different path for no client benefit.
- A new `PATCH /clients/{clientId}/profile` endpoint — rejected as scope creep; the existing `PUT` is already semantically correct (idempotent replacement of a single field).

---

## Decision 2: Client self-read of profile (FR-003)

**Decision**: Add a new `GET /clients/{clientId}/profile` endpoint accessible by `X-Client-Key` with ownership check. It returns a focused subset of the admin `ClientResponse`: `id`, `displayName`, `isActive`, `createdAt`, and `reviewerId`.

**Rationale**: The existing `GET /clients/{clientId}` is admin-only and returns ADO credential metadata that clients must not see (`hasAdoCredentials` being a minor disclosure risk). Adding a distinct profile endpoint makes the authorization contract explicit and avoids changing the semantics of the admin endpoint. The response shape mirrors `ClientResponse` minus sensitive admin fields.

**Alternatives considered**:
- Extending `GET /clients/{clientId}` to accept both admin and client keys — rejected because the admin response includes `hasAdoCredentials` (which leaks infrastructure knowledge to clients) and because the dual-auth pattern on a GET would require refactoring existing admin tests.
- Returning `reviewerId` only via a dedicated `GET /clients/{clientId}/reviewer-identity` — rejected as too granular; the settings extension needs at minimum `displayName` and `isActive` alongside `reviewerId` to render its UI.

---

## Decision 3: Audit logging actor type (FR-007)

**Decision**: Log the actor type (`Admin` vs `Client`) at the controller layer via a `[LoggerMessage]`-attributed partial method on a partial `ClientsController` class. The log entry includes `clientId` and actor type as structured fields.

**Rationale**: The controller already knows which auth path was taken (admin key present vs. client key present). Logging here avoids leaking auth knowledge into the Application or Infrastructure layers, preserving Clean Architecture boundaries. The `[LoggerMessage]` requirement from the constitution is satisfied.

**Alternatives considered**:
- Adding an actor parameter to `IClientAdminService.SetReviewerIdentityAsync` — rejected because Application should not know about authentication mechanisms.
- Logging in `PostgresClientAdminService` — rejected (same reason; Infrastructure should not know about callers).

---

## Decision 4: No new infrastructure or DB changes

**Decision**: Zero schema changes; zero new Application interfaces or Infrastructure implementations required.

**Rationale**:
- `reviewer_id` column already exists on `clients` table (migration `20260311143353_AddReviewerIdToClients_RemoveFromCrawlConfigs`).
- `IClientAdminService.SetReviewerIdentityAsync` and `GetByIdAsync` already exist and are wired in `PostgresClientAdminService`.
- `IClientRegistry.GetClientIdByKeyAsync` already exists for ownership validation.

**Implications**: All changes are contained in `MeisterProPR.Api` (controller + validator registration check). No migrations, no new service interfaces, no new DTOs.

---

## Decision 6: Identity resolution responsibility

**Decision**: Identity resolution (translating an ADO display name to a VSS identity GUID) is performed entirely within the Azure DevOps settings extension using the ADO Extension SDK. The backend does not expose an identity resolution endpoint.

**Rationale**: The ADO `_apis/identities` and `_apis/identitypicker/identities` endpoints do not provide reliable prefix or fuzzy matching for service principal identities when called from a backend service principal context — they require the exact full account name regardless of the `searchFilter` used. The ADO Extension SDK, running in-browser within the ADO web context, has first-class access to the identity picker typeahead API and the user's authentication context, making it the correct layer for this lookup.

**Alternatives considered**:
- Backend-proxied identity search via `_apis/identities` with `searchFilter=General` — rejected; only exact matches returned for service principal identities.
- Backend-proxied identity search via `_apis/identitypicker/identities` with `identityTypes=["user","group","svc"]` — rejected; partial queries consistently returned empty results for the service principal identity type tested, even after adding the `svc` type and a storage-key fallback to resolve `localId=Guid.Empty` entries.
- Listing all graph users/service principals and filtering client-side — rejected; prohibitively expensive for large organisations and requires additional Graph API permissions.

**Implications**: The `GET /identities/resolve` endpoint and its entire implementation chain (`IIdentityResolver`, `AdoIdentityResolver`, `StubIdentityResolver`) have been removed from the backend. The backend only accepts a pre-resolved GUID via `PUT /clients/{clientId}/reviewer-identity`.

---

## Decision 5: Response shape for client profile

**Decision**: Introduce a new `ClientProfileResponse` record in `ClientsController` with fields: `Id`, `DisplayName`, `IsActive`, `CreatedAt`, `ReviewerId`. This is a strict subset of `ClientResponse` with `HasAdoCredentials` omitted.

**Rationale**: Omitting `HasAdoCredentials` ensures clients cannot infer admin-managed infrastructure state. Using a dedicated record (not a subclass of `ClientResponse`) keeps the types independent and allows each to evolve separately without coupling.
