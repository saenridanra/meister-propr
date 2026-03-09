# API Contract Changes: Per-Client ADO Identity (003-client-ado-auth)

All changes are **non-breaking additions** to the existing OpenAPI contract. No existing fields are removed or renamed. Existing clients sending unmodified requests continue to work.

---

## Modified Endpoints

### `POST /clients` — Create Client

**Change**: `CreateClientRequest` body is unchanged. ADO credentials are managed via the dedicated credential endpoints (see below). No change to request schema.

**Response change**: `ClientResponse` now includes `hasAdoCredentials`, `adoTenantId`, `adoClientId` (all added fields). For newly created clients these will be `false` / `null`.

---

### `GET /clients` — List Clients

**Response change**: Each item in the array now includes `hasAdoCredentials`, `adoTenantId`, `adoClientId`.

---

### `GET /clients/{clientId}` — Get Client

**Response change**: Response now includes `hasAdoCredentials`, `adoTenantId`, `adoClientId`.

---

### `PATCH /clients/{clientId}` — Update Client Status

**No change** to request or response schema beyond the new `ClientResponse` fields.

---

## New Endpoints

### `PUT /clients/{clientId}/ado-credentials`

Sets (creates or replaces) the ADO service principal credentials for a client.

**Auth**: `X-Admin-Key` required.

**Request**:
```json
{
  "tenantId": "string",
  "clientId": "string",
  "secret":   "string"
}
```

All three fields are required. If any is missing or blank, returns `400 Bad Request`.

**Responses**:

| Code | Meaning                                        |
|------|------------------------------------------------|
| 204  | Credentials stored successfully                |
| 400  | One or more required fields missing or blank   |
| 401  | Missing or invalid `X-Admin-Key`               |
| 404  | Client not found                               |

---

### `DELETE /clients/{clientId}/ado-credentials`

Clears ADO credentials from a client. After this, the client uses the global backend identity.

**Auth**: `X-Admin-Key` required.

**Request body**: none.

**Responses**:

| Code | Meaning                                              |
|------|------------------------------------------------------|
| 204  | Credentials cleared (or client had none — idempotent)|
| 401  | Missing or invalid `X-Admin-Key`                     |
| 404  | Client not found                                     |

---

## Updated `ClientResponse` Schema

```json
{
  "id":                "uuid",
  "displayName":       "string",
  "isActive":          "boolean",
  "createdAt":         "datetime",
  "hasAdoCredentials": "boolean",
  "adoTenantId":       "string | null",
  "adoClientId":       "string | null"
}
```

Note: `adoClientSecret` / `secret` is **never present** in any response.

---

## OpenAPI Version

- Change type: **non-breaking** (additive only — new optional response fields, new endpoints)
- No version bump required
- `openapi.json` must be regenerated and committed after implementation
