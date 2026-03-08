# API Contract Additions: PR Review Auto-Assignment & PostgreSQL Persistence

**Feature**: `002-pr-review-persistence`
**Date**: 2026-03-08
**Type**: Non-breaking additions to existing contract

All existing endpoints (`POST /reviews`, `GET /reviews`, `GET /reviews/{jobId}`) are **unchanged**.

---

## Authentication & Authorization Model

Two authentication roles are introduced:

| Role | Header | Who holds it | What it grants |
|------|--------|--------------|----------------|
| **Administrator** | `X-Admin-Key` | Backend operator (single key from env var) | Full client management, global job list |
| **Client** | `X-Client-Key` | Registered API consumer | Manage own crawl configurations, submit/view own reviews |

- **Admin endpoints** require `X-Admin-Key` matching the configured administrator key. Unknown or missing key → `401`.
- **Client endpoints** require `X-Client-Key` matching a registered, active client. Unknown or missing key → `401`.
- A client can only manage their own crawl configurations (scoped by `clientId` derived from their key).

---

## New: Client Management Endpoints (Admin Only)

### POST /clients

Register a new client. Requires `X-Admin-Key`.

**Request body**:
```json
{
  "key": "string (min 16 chars)",
  "displayName": "string"
}
```

**Responses**:
- `201 Created`
  ```json
  {
    "id": "uuid",
    "displayName": "string",
    "isActive": true,
    "createdAt": "ISO8601"
  }
  ```
  Note: `key` is **not** echoed back in the response (security).
- `409 Conflict` — key already registered
- `400 Bad Request` — validation failure

---

### GET /clients

List all registered clients. Requires `X-Admin-Key`.

**Responses**:
- `200 OK`
  ```json
  [
    {
      "id": "uuid",
      "displayName": "string",
      "isActive": true,
      "createdAt": "ISO8601"
    }
  ]
  ```
  Note: `key` values are **never** returned (security: no secret echo).

---

### PATCH /clients/{clientId}

Enable or disable a client. Requires `X-Admin-Key`.

**Request body**:
```json
{ "isActive": false }
```

**Responses**:
- `200 OK` — updated client (same shape as `GET /clients` item)
- `404 Not Found`

---

## New: Crawl Configuration Endpoints (Client-Scoped)

A client may manage their **own** crawl configurations. The `clientId` in the path must correspond to the identity of the caller's `X-Client-Key`; a client cannot read or modify another client's configurations.

### POST /clients/{clientId}/crawl-configurations

Add a crawl target. Requires `X-Client-Key` (caller must own the `clientId`).

**Request body**:
```json
{
  "organizationUrl": "https://dev.azure.com/myorg",
  "projectId": "string",
  "reviewerId": "uuid (ADO identity GUID of the service account reviewer)",
  "crawlIntervalSeconds": 60
}
```

**Responses**:
- `201 Created`
  ```json
  {
    "id": "uuid",
    "clientId": "uuid",
    "organizationUrl": "string",
    "projectId": "string",
    "reviewerId": "uuid",
    "crawlIntervalSeconds": 60,
    "isActive": true,
    "createdAt": "ISO8601"
  }
  ```
- `403 Forbidden` — caller does not own this `clientId`
- `404 Not Found` — `clientId` does not exist
- `400 Bad Request` — validation failure (missing fields, invalid URL, interval < 10)

---

### GET /clients/{clientId}/crawl-configurations

List all crawl configurations for a client. Requires `X-Client-Key` (caller must own the `clientId`).

**Responses**:
- `200 OK` — array of crawl configuration objects (same shape as POST 201 response)
- `403 Forbidden`
- `404 Not Found`

---

### PATCH /clients/{clientId}/crawl-configurations/{configId}

Enable or disable a crawl configuration. Requires `X-Client-Key` (caller must own the `clientId`).

**Request body**:
```json
{ "isActive": false }
```

**Responses**:
- `200 OK` — updated configuration object
- `403 Forbidden`
- `404 Not Found`

---

## New: Global Job List Endpoint (Admin Only)

### GET /jobs

Return all review jobs across all clients, newest first. Intended for operator dashboards. Requires `X-Admin-Key`.

**Query parameters**:
- `limit` (int, default 100, max 1000)
- `offset` (int, default 0)
- `status` (string, optional) — filter by `Pending|Processing|Completed|Failed`

**Responses**:
- `200 OK`
  ```json
  {
    "total": 42,
    "items": [
      {
        "id": "uuid",
        "clientId": "uuid or null",
        "organizationUrl": "string",
        "projectId": "string",
        "repositoryId": "string",
        "pullRequestId": 123,
        "iterationId": 1,
        "status": "Completed",
        "submittedAt": "ISO8601",
        "completedAt": "ISO8601 or null",
        "resultSummary": "string or null",
        "errorMessage": "string or null"
      }
    ]
  }
  ```

---

## openapi.json Update

The `openapi.json` at repository root must be regenerated after implementing these endpoints. This is a non-breaking addition (no existing paths or schemas are modified).

### New environment variable

| Variable | Purpose | Required |
|----------|---------|----------|
| `MEISTER_ADMIN_KEY` | Single administrator API key for admin-only endpoints | Yes |
