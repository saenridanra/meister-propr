# API Contracts: Admin Management UI

**Feature**: 004-admin-ui | **Date**: 2026-03-09

The SPA makes calls against the **existing** backend REST API. No new endpoints are introduced.
This document captures which endpoints are used, the expected request/response shapes, and
error-handling requirements for the frontend.

All requests include the header:

```
X-Admin-Key: <adminKey from sessionStorage>
```

The backend base URL is configured via `VITE_API_BASE_URL` (empty string = same origin, used in
Docker; `https://localhost:5443` for local dev pointing at nginx).

---

## Client Endpoints

### Register a Client

```
POST /clients
Content-Type: application/json

{
  "key": "my-secret-key",
  "displayName": "My Client"
}
```

**Success**: `201 Created`
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "displayName": "My Client",
  "isActive": true,
  "hasAdoCredentials": false,
  "adoTenantId": null,
  "adoClientId": null,
  "createdAt": "2026-03-09T10:00:00Z"
}
```

**Error responses**:
| Status | Trigger                  | UI behaviour                        |
|--------|--------------------------|-------------------------------------|
| `400`  | Missing/invalid fields   | Show inline field error             |
| `401`  | Wrong admin key          | Clear session → redirect to login   |
| `409`  | Key already in use       | Show "key already registered" error |

---

### List Clients

```
GET /clients
```

**Success**: `200 OK`
```json
[
  {
    "id": "3fa85f64-...",
    "displayName": "My Client",
    "isActive": true,
    "hasAdoCredentials": true,
    "adoTenantId": "aaaabbbb-...",
    "adoClientId": "ccccdddd-...",
    "createdAt": "2026-03-09T10:00:00Z"
  }
]
```

**Error responses**:
| Status | Trigger         | UI behaviour                        |
|--------|-----------------|-------------------------------------|
| `401`  | Wrong admin key | Clear session → redirect to login   |

---

### Get Client

```
GET /clients/{id}
```

**Success**: `200 OK` — same shape as a single element from the list response.

**Error responses**:
| Status | Trigger           | UI behaviour                        |
|--------|-------------------|-------------------------------------|
| `401`  | Wrong admin key   | Clear session → redirect to login   |
| `404`  | Client not found  | Show "client not found" and navigate back to list |

---

### Update Client

```
PATCH /clients/{id}
Content-Type: application/json

{
  "displayName": "New Name",   // optional
  "isActive": false            // optional
}
```

**Success**: `200 OK` — updated client object.

**Error responses**:
| Status | Trigger           | UI behaviour                        |
|--------|-------------------|-------------------------------------|
| `400`  | Invalid fields    | Show inline field error             |
| `401`  | Wrong admin key   | Clear session → redirect to login   |
| `404`  | Client not found  | Show "client not found" error       |

---

### Delete Client

```
DELETE /clients/{id}
```

**Success**: `204 No Content`

**Error responses**:
| Status | Trigger           | UI behaviour                        |
|--------|-------------------|-------------------------------------|
| `401`  | Wrong admin key   | Clear session → redirect to login   |
| `404`  | Client not found  | Show "already deleted" message; refresh list |

---

## ADO Credentials Endpoints

### Set ADO Credentials

```
PUT /clients/{id}/ado-credentials
Content-Type: application/json

{
  "tenantId": "aaaabbbb-cccc-dddd-eeee-ffffffffffff",
  "clientId": "11112222-3333-4444-5555-666677778888",
  "secret":   "my-client-secret"
}
```

**Success**: `204 No Content`

**Error responses**:
| Status | Trigger           | UI behaviour                        |
|--------|-------------------|-------------------------------------|
| `400`  | Missing fields    | Show inline field error             |
| `401`  | Wrong admin key   | Clear session → redirect to login   |
| `404`  | Client not found  | Show "client not found" error       |

---

### Clear ADO Credentials

```
DELETE /clients/{id}/ado-credentials
```

**Success**: `204 No Content`

**Error responses**:
| Status | Trigger           | UI behaviour                        |
|--------|-------------------|-------------------------------------|
| `401`  | Wrong admin key   | Clear session → redirect to login   |
| `404`  | Client not found  | Show "client not found" error       |

---

## Admin Key Verification (Login)

There is no dedicated login endpoint. The SPA verifies the admin key by calling `GET /clients`
with the entered key as `X-Admin-Key`. A `200 OK` response confirms the key is valid. A `401`
response means the key is wrong.

This avoids a dedicated login endpoint while still verifying the key against the backend.

---

## CORS Configuration

| Environment    | Origin                  | Mechanism                                |
|----------------|-------------------------|------------------------------------------|
| Docker (nginx) | `https://localhost:5443`| Same origin — nginx serves both SPA and API; no CORS needed |
| Local Vite dev | `http://localhost:5173` | Vite dev server proxy (`/clients`, `/reviews`, etc. → backend); no CORS needed |
| Other deployments | custom origin        | Add to `CORS_ORIGINS` env var (comma-separated); existing backend mechanism handles it |

The backend's existing `CORS_ORIGINS` env-var mechanism requires **no code changes**.
