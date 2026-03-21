# Data Model: Client Self-Managed Reviewer Identity

**Feature**: 010-client-reviewer-identity
**Date**: 2026-03-21

---

## Schema changes

**None.** The `reviewer_id` column already exists on the `clients` table (added by migration `20260311143353_AddReviewerIdToClients_RemoveFromCrawlConfigs`). No new migrations are required.

---

## Existing entity: Client (`clients` table)

| Column           | Type          | Nullable | Notes                                                        |
|------------------|---------------|----------|--------------------------------------------------------------|
| `id`             | `uuid`        | No       | Primary key                                                  |
| `key`            | `text`        | No       | Secret API key; never returned in responses                  |
| `display_name`   | `text`        | No       | Human-readable name                                          |
| `is_active`      | `boolean`     | No       | Whether the client may authenticate                          |
| `created_at`     | `timestamptz` | No       | Creation timestamp (UTC)                                     |
| `ado_tenant_id`  | `text`        | Yes      | Azure DevOps tenant — admin-managed                         |
| `ado_client_id`  | `text`        | Yes      | Azure DevOps client ID — admin-managed                      |
| `ado_client_secret` | `text`    | Yes      | Azure DevOps secret — admin-managed; never logged            |
| `reviewer_id`    | `uuid`        | Yes      | ADO identity GUID for the AI reviewer; `null` until set     |

### Reviewer identity lifecycle

```
[not set] ──PUT (admin or client)──► [GUID stored]
          ◄──── (overwrite) ─────────
```

- A client may set, replace, or re-set `reviewer_id` at any time.
- There is no "clear" operation in this feature — the field is overwritten, not deleted.
- A `null` `reviewer_id` causes review jobs to fail at execution time (existing behaviour, unchanged).

---

## Application-layer DTO (unchanged)

`ClientDto` (in `MeisterProPR.Application.DTOs`) already carries `Guid? ReviewerId`. No changes required.

---

## New API response shape

`ClientProfileResponse` (new record in `MeisterProPR.Api.Controllers`):

| Field         | Type            | Notes                                       |
|---------------|-----------------|---------------------------------------------|
| `Id`          | `Guid`          | Client identifier                           |
| `DisplayName` | `string`        | Human-readable name                         |
| `IsActive`    | `bool`          | Whether the client is active                |
| `CreatedAt`   | `DateTimeOffset`| UTC creation timestamp                      |
| `ReviewerId`  | `Guid?`         | ADO identity GUID; `null` if not yet set    |

`HasAdoCredentials` is intentionally omitted — it reveals admin-managed infrastructure state that clients must not see.
