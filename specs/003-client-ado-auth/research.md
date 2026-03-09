# Research: Per-Client ADO Identity (003-client-ado-auth)

## Credential Isolation Architecture

**Decision**: Store the three ADO service principal fields (`AdoTenantId`, `AdoClientId`, `AdoClientSecret`) directly on `ClientRecord` as three nullable columns. Credentials are an atomic group — valid only when all three are non-null/non-empty.

**Rationale**: Avoids a separate `client_ado_credentials` join table for a 1:0..1 relationship with no additional attributes. The `ClientRecord` already owns all client-scoped configuration. A FK table would add a join with no query flexibility benefit. The atomicity constraint is enforced in the API layer (all-or-nothing validation) and implicitly by the nullable group.

**Alternatives considered**:
- Separate `ClientAdoCredentials` table (1:0..1) — adds unnecessary JOIN for every credential lookup with no benefit at this stage.
- Vault reference string instead of plaintext — deferred; explicit assumption in spec.md. DB-level access controls are sufficient for the current threat model.

---

## Application Interface Design: `IClientAdoCredentialRepository`

**Decision**: Introduce a new `IClientAdoCredentialRepository` interface in the Application layer, separate from `IClientRegistry`.

**Rationale**: `IClientRegistry` is a narrow, authentication-purpose interface (key → ID, key → valid). Adding credential management to it would violate the Interface Segregation Principle and force `EnvVarClientRegistry` to implement methods it has no meaningful implementation for. A separate interface keeps each concern focused.

**Interface**:
```csharp
// Application.Interfaces
interface IClientAdoCredentialRepository
{
    Task<ClientAdoCredentials?> GetByClientIdAsync(Guid clientId, CancellationToken ct);
    Task UpsertAsync(Guid clientId, ClientAdoCredentials credentials, CancellationToken ct);
    Task ClearAsync(Guid clientId, CancellationToken ct);
}

// Application.DTOs
record ClientAdoCredentials(string TenantId, string ClientId, string Secret);
```

**Non-DB mode**: Register `NullClientAdoCredentialRepository` (always returns `null`) so stub/legacy mode falls back to global credentials without code changes in ADO services.

---

## Credential Flow: From Client Record to VssConnection

**Decision**: Change `VssConnectionFactory.GetConnectionAsync` to accept an explicit `ClientAdoCredentials?` parameter. The factory resolves it to `ClientSecretCredential` when present, otherwise falls back to the global `TokenCredential` held at construction. Cache key changes to `$"{orgUrl}::{credentials?.ClientId ?? "global"}"`.

**Rationale**: This keeps credential resolution in the Infrastructure layer (the only layer that knows about `Azure.Core.TokenCredential`), avoids passing Azure types through the Application layer, and maintains the single-responsibility of the factory.

**ADO services (AdoAssignedPrFetcher, AdoPullRequestFetcher, AdoCommentPoster)**: Each injects `IClientAdoCredentialRepository` as a new dependency. Before calling `connectionFactory.GetConnectionAsync`, they call `credentialRepo.GetByClientIdAsync(config.ClientId, ct)` and pass the result to the factory.

**AdoIdentityResolver**: Same pattern — called from `ClientsController.AddCrawlConfiguration` which already has `clientId` in scope. Add `clientId` as a parameter to `IIdentityResolver.ResolveAsync`.

---

## API Contract Changes

**Decision**: Extend existing `POST /clients` and `PATCH /clients/{id}` endpoints with optional ADO credential fields. Add `HasAdoCredentials`, `AdoTenantId`, and `AdoClientId` to `ClientResponse` (never the secret). Create dedicated `PUT /clients/{id}/ado-credentials` and `DELETE /clients/{id}/ado-credentials` endpoints for explicit credential management.

**Rationale**: Separating credential management to dedicated endpoints follows REST resource semantics — credentials are a sub-resource of the client. The `PATCH /clients/{id}` remains for `IsActive` toggling only. `PUT` (full replacement semantics) is appropriate for credential upsert because credentials are always replaced as a unit. `DELETE` for clearing aligns with the spec's "explicit clear" requirement.

**What is exposed in responses**:
- `HasAdoCredentials: bool` — presence indicator
- `AdoTenantId: string?` — non-sensitive, useful for ops verification
- `AdoClientId: string?` — non-sensitive, useful for ops verification
- Secret: **never returned**, not even masked; the field is absent from all response types.

**Alternatives considered**:
- Extend `PATCH /clients/{id}` with credential fields — mixes two different concerns in one PATCH, complicates partial-update semantics. Rejected.
- Mask secret as `"****"` — could mislead callers into sending `"****"` back as an update. Omitting is safer.

---

## IIdentityResolver Interface Change

**Decision**: Add `Guid clientId` parameter to `IIdentityResolver.ResolveAsync`.

**Rationale**: The identity resolver calls the org-scoped VSSPS endpoint using a bearer token. If the client has its own service principal, that SP must be used — the global identity may not have permission in the client's Azure tenant. `ClientsController` already has `clientId` in scope when calling `ResolveAsync` so the caller change is trivial.

**Signature change**:
```csharp
// before
Task<IReadOnlyList<ResolvedIdentity>> ResolveAsync(string orgUrl, string displayName, CancellationToken ct);
// after
Task<IReadOnlyList<ResolvedIdentity>> ResolveAsync(string orgUrl, string displayName, Guid clientId, CancellationToken ct);
```

---

## Security: Serilog Scrubbing

**Decision**: Add `AdoClientSecret` to the Serilog destructuring policy scrub list alongside the existing `X-Client-Key`, `X-Ado-Token`, and `AZURE_CLIENT_SECRET` entries.

**Rationale**: If a `ClientAdoCredentials` object is accidentally destructured in a log statement, the secret must not appear. Named property scrubbing catches this.

---

## DB Migration Strategy

**Decision**: Add a single EF Core migration `AddClientAdoCredentials` that adds three nullable `text` columns (`ado_tenant_id`, `ado_client_id`, `ado_client_secret`) to the `clients` table. All are nullable to support existing rows and clients without per-client credentials.

**No index** on these columns — lookups are by primary key (`id`) only.

---

## EnvVar / Legacy Mode Behaviour

**Decision**: In non-DB mode (`DB_CONNECTION_STRING` absent), register `NullClientAdoCredentialRepository` which always returns `null`. The ADO services transparently fall back to the global credential. No behaviour change for existing deployments.
