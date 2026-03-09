# Data Model: Per-Client ADO Identity (003-client-ado-auth)

## Changed Entities

### `ClientRecord` (Infrastructure — EF Core persistence model)

**Existing fields** (unchanged): `Id`, `Key`, `DisplayName`, `IsActive`, `CreatedAt`

**New nullable fields**:

| Column (DB)        | Property (.NET)     | Type       | Nullable | Notes                                           |
|--------------------|---------------------|------------|----------|-------------------------------------------------|
| `ado_tenant_id`    | `AdoTenantId`       | `string?`  | yes      | Azure AD tenant GUID or domain                  |
| `ado_client_id`    | `AdoClientId`       | `string?`  | yes      | Azure AD application (client) GUID              |
| `ado_client_secret`| `AdoClientSecret`   | `string?`  | yes      | Client secret value — NEVER returned in API     |

**Invariant**: either all three are non-null/non-empty, or all three are null. Partial state is rejected by the API layer before reaching the repository.

---

## New DTOs (Application layer)

### `ClientAdoCredentials`

```
record ClientAdoCredentials(
    string TenantId,     // Azure AD tenant ID
    string ClientId,     // Azure AD application (client) ID
    string Secret        // Client secret — never serialised to JSON responses
)
```

This DTO crosses the Application → Infrastructure boundary to pass the raw credential data. The Infrastructure layer converts it to `Azure.Core.TokenCredential` (`ClientSecretCredential`). The Application layer never sees `TokenCredential`.

---

## New Interfaces (Application layer)

### `IClientAdoCredentialRepository`

Manages the ADO credential sub-resource attached to a client.

```
interface IClientAdoCredentialRepository
{
    // Returns null if the client has no per-client credentials configured.
    Task<ClientAdoCredentials?> GetByClientIdAsync(Guid clientId, CancellationToken ct);

    // Creates or replaces the credentials for a client.
    // Precondition: client with clientId exists.
    Task UpsertAsync(Guid clientId, ClientAdoCredentials credentials, CancellationToken ct);

    // Removes credentials — client falls back to global identity.
    Task ClearAsync(Guid clientId, CancellationToken ct);
}
```

**Implementations**:
- `PostgresClientAdoCredentialRepository` — reads/writes the three nullable columns on `ClientRecord` via EF Core (DB mode)
- `NullClientAdoCredentialRepository` — always returns `null`, no-ops for upsert/clear (legacy/stub mode)

---

## Changed Interfaces (Application layer)

### `IIdentityResolver`

Add `clientId` parameter so the Infrastructure implementation can resolve the correct credential:

```
// Before
Task<IReadOnlyList<ResolvedIdentity>> ResolveAsync(string orgUrl, string displayName, CancellationToken ct);

// After
Task<IReadOnlyList<ResolvedIdentity>> ResolveAsync(string orgUrl, string displayName, Guid clientId, CancellationToken ct);
```

---

## Changed Infrastructure Types

### `VssConnectionFactory`

Cache key changes from `orgUrl` to `$"{orgUrl}::{credentials?.ClientId ?? "global"}"`.

```
// Before
Task<VssConnection> GetConnectionAsync(string organizationUrl, CancellationToken ct);

// After
Task<VssConnection> GetConnectionAsync(string organizationUrl, ClientAdoCredentials? credentials, CancellationToken ct);
```

Internally:
- If `credentials != null` → resolve `ClientSecretCredential(credentials.TenantId, credentials.ClientId, credentials.Secret)`
- Else → use the global `TokenCredential` held as a constructor parameter (unchanged injection)

The global `TokenCredential` still comes from `InfrastructureServiceExtensions.ResolveCredential(configuration)` at startup. The change is purely additive — the factory now also supports a per-call override.

---

## EF Core Migration

**Migration name**: `AddClientAdoCredentials`

**Up**:
```sql
ALTER TABLE clients
    ADD COLUMN ado_tenant_id     text,
    ADD COLUMN ado_client_id     text,
    ADD COLUMN ado_client_secret text;
```

**Down**:
```sql
ALTER TABLE clients
    DROP COLUMN ado_tenant_id,
    DROP COLUMN ado_client_id,
    DROP COLUMN ado_client_secret;
```

All columns are nullable → zero downtime migration; existing rows are unaffected.

---

## API Request / Response Shape Changes

### `ClientResponse` (returned by GET/POST/PATCH /clients endpoints)

**New fields added** (non-breaking addition):

| Field              | Type      | Notes                                       |
|--------------------|-----------|---------------------------------------------|
| `hasAdoCredentials`| `bool`    | True if all three credential fields are set |
| `adoTenantId`      | `string?` | Null when no credentials configured         |
| `adoClientId`      | `string?` | Null when no credentials configured         |

Secret is **never present** in any response type.

### `PUT /clients/{clientId}/ado-credentials` — request body

```json
{
  "tenantId": "string (required)",
  "clientId": "string (required)",
  "secret":   "string (required)"
}
```

All three fields required. Returns `204 No Content` on success.

### `DELETE /clients/{clientId}/ado-credentials`

No body. Returns `204 No Content` on success. Returns `404 Not Found` if client does not exist.
