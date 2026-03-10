# Data Model: Admin Management UI

**Feature**: 004-admin-ui | **Date**: 2026-03-09

This feature introduces no new backend entities. The SPA consumes existing backend entities
via the REST API. This document captures the **frontend type model** — the TypeScript
interfaces that mirror the API contracts and the client-side session state.

---

## Frontend Types

### `Client`

Represents a registered API client returned by `GET /clients` and `GET /clients/{id}`.

| Field               | Type      | Nullable | Description                                                |
|---------------------|-----------|----------|------------------------------------------------------------|
| `id`                | `string`  | No       | UUID — primary key                                         |
| `displayName`       | `string`  | No       | Human-readable name shown in the admin UI                  |
| `isActive`          | `boolean` | No       | Whether the client can authenticate against the backend    |
| `hasAdoCredentials` | `boolean` | No       | Whether per-client ADO credentials are stored              |
| `adoTenantId`       | `string`  | Yes      | Azure tenant ID if credentials are set; `null` otherwise   |
| `adoClientId`       | `string`  | Yes      | Azure service principal app ID; `null` if not set          |
| `createdAt`         | `string`  | No       | ISO-8601 timestamp                                         |

> `adoClientSecret` is **never present** — the backend enforces this at the serialization layer.

---

### `CreateClientRequest`

Payload sent to `POST /clients`.

| Field         | Type     | Constraints                       |
|---------------|----------|-----------------------------------|
| `key`         | `string` | Required, non-empty               |
| `displayName` | `string` | Required, non-empty               |

---

### `UpdateClientRequest`

Payload sent to `PATCH /clients/{id}`.

| Field         | Type      | Constraints              |
|---------------|-----------|--------------------------|
| `displayName` | `string`  | Optional, non-empty      |
| `isActive`    | `boolean` | Optional                 |

---

### `AdoCredentialsRequest`

Payload sent to `PUT /clients/{id}/ado-credentials`.

| Field      | Type     | Constraints             |
|------------|----------|-------------------------|
| `tenantId` | `string` | Required, non-empty     |
| `clientId` | `string` | Required, non-empty     |
| `secret`   | `string` | Required, non-empty; rendered as `type="password"`, never read back |

---

### `AdminSession`

Client-side session held in `sessionStorage`. Cleared on tab close, logout, or 401 response.

| Field      | Type     | Storage         | Description                                    |
|------------|----------|-----------------|------------------------------------------------|
| `adminKey` | `string` | `sessionStorage` | The value of the `X-Admin-Key` header sent with each API request |

**Key**: `"meisterpropr_admin_key"` in `sessionStorage`.

---

## State Transitions

### Client status

```
(created) → isActive: true
      │
      ▼
  isActive ──[disable]──► isActive: false
     ▲                          │
     └─────[enable]─────────────┘

  (any state) ──[delete]──► (removed)
```

### ADO credentials status on a client

```
hasAdoCredentials: false
      │
      ▼ PUT /clients/{id}/ado-credentials
hasAdoCredentials: true  (tenantId + clientId visible; secret write-only)
      │
      ▼ DELETE /clients/{id}/ado-credentials
hasAdoCredentials: false
```

### Admin session lifecycle

```
sessionStorage empty → LoginView
      │
      ▼ (correct admin key entered)
sessionStorage["meisterpropr_admin_key"] = key → ClientsView
      │
      ├─ (logout clicked) ──────────────────────────────► sessionStorage cleared → LoginView
      │
      └─ (API returns 401) ─────────────────────────────► sessionStorage cleared → LoginView
```

---

## Validation Rules (frontend)

| Entity              | Field         | Rule                                     |
|---------------------|---------------|------------------------------------------|
| `CreateClientRequest` | `key`       | Required; non-empty; no leading/trailing whitespace |
| `CreateClientRequest` | `displayName` | Required; non-empty                   |
| `UpdateClientRequest` | `displayName` | If present, non-empty                 |
| `AdoCredentialsRequest` | `tenantId`, `clientId`, `secret` | All required; non-empty |
| `AdminSession`      | `adminKey`    | Required; non-empty; submitted as-is to the backend for verification |

Backend validation is the authoritative source; frontend validation is a UX convenience only.
