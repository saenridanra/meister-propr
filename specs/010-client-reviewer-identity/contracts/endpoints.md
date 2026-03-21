# API Contracts: Client Self-Managed Reviewer Identity

**Feature**: 010-client-reviewer-identity
**Date**: 2026-03-21

All endpoints are relative to the API base URL. Authentication headers are mutually exclusive per call — send one or the other, not both.

---

## New endpoint: Get client profile (client-accessible)

```
GET /clients/{clientId}/profile
```

**Authentication**: `X-Client-Key` header (caller must own `{clientId}`)

**Path parameters**:
| Parameter  | Type   | Required | Description        |
|------------|--------|----------|--------------------|
| `clientId` | `guid` | Yes      | Client identifier  |

**Success response — 200 OK**:

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "displayName": "My Team",
  "isActive": true,
  "createdAt": "2026-03-21T10:00:00Z",
  "reviewerId": "7c9e6679-7425-40de-944b-e07fc1f90ae7"
}
```

`reviewerId` is `null` when no reviewer identity has been set.

**Error responses**:
| Code | Condition                                              |
|------|--------------------------------------------------------|
| 401  | `X-Client-Key` header is missing or invalid            |
| 403  | Caller's key does not match `{clientId}`               |
| 404  | No client found with `{clientId}`                      |

---

## Modified endpoint: Set reviewer identity (extended to accept client key)

```
PUT /clients/{clientId}/reviewer-identity
```

**Authentication** (either is accepted):
- `X-Admin-Key` header — may set identity for any client (existing behaviour, unchanged)
- `X-Client-Key` header — may only set identity for the client that owns the key

**Path parameters**:
| Parameter  | Type   | Required | Description        |
|------------|--------|----------|--------------------|
| `clientId` | `guid` | Yes      | Client identifier  |

**Request body**:

```json
{
  "reviewerId": "7c9e6679-7425-40de-944b-e07fc1f90ae7"
}
```

| Field        | Type   | Required | Validation                         |
|--------------|--------|----------|------------------------------------|
| `reviewerId` | `guid` | Yes      | Must be a non-empty (non-zero) GUID |

**Success response — 204 No Content** (idempotent — same value accepted without error)

**Error responses**:
| Code | Condition                                                      |
|------|----------------------------------------------------------------|
| 400  | `reviewerId` is absent or is the zero GUID                     |
| 401  | Neither `X-Admin-Key` nor `X-Client-Key` header is valid       |
| 403  | `X-Client-Key` provided but does not match `{clientId}`        |
| 404  | No client found with `{clientId}`                              |

**Behaviour change**: Previously rejected any request without `X-Admin-Key`. Now also accepts `X-Client-Key` with an ownership check. Admin behaviour is fully unchanged.

---

## Unchanged endpoints (reference)

The following endpoints are unaffected by this feature:

| Method | Path                                          | Auth      | Notes                          |
|--------|-----------------------------------------------|-----------|--------------------------------|
| GET    | `/clients`                                    | Admin     | Lists all clients              |
| GET    | `/clients/{clientId}`                         | Admin     | Full admin client view         |
| POST   | `/clients`                                    | Admin     | Creates a client               |
| PATCH  | `/clients/{clientId}`                         | Admin     | Updates display name / active  |
| DELETE | `/clients/{clientId}`                         | Admin     | Deletes client                 |
| PUT    | `/clients/{clientId}/ado-credentials`         | Admin     | Sets ADO service principal     |
| DELETE | `/clients/{clientId}/ado-credentials`         | Admin     | Clears ADO service principal   |
**Note on identity resolution**: The `reviewerId` GUID must be resolved before calling `PUT /clients/{clientId}/reviewer-identity`. This resolution is performed client-side using the Azure DevOps Extension SDK (not via a backend endpoint). The backend has no identity resolution endpoint.
