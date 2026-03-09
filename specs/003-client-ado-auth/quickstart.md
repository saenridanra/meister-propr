# Quickstart: Per-Client ADO Identity (003-client-ado-auth)

## Prerequisites

- Backend running with `DB_CONNECTION_STRING` set (per-client credentials require DB mode)
- `MEISTER_ADMIN_KEY` set for admin operations
- A client already registered, or about to be registered
- An Azure AD service principal created in the customer's tenant with the following permissions granted in their ADO org:
  - Member of the relevant ADO project(s)
  - "Basic" or higher access level

## 1. Register a client without credentials (existing behaviour, unchanged)

```http
POST /clients
X-Admin-Key: <admin-key>
Content-Type: application/json

{
  "key": "my-secret-client-key-16chars+",
  "displayName": "Contoso Engineering"
}
```

Response includes `"hasAdoCredentials": false`.

## 2. Attach ADO credentials to an existing client

```http
PUT /clients/{clientId}/ado-credentials
X-Admin-Key: <admin-key>
Content-Type: application/json

{
  "tenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "clientId": "yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy",
  "secret":   "the-client-secret-value"
}
```

Response: `204 No Content`

Verify by fetching the client:

```http
GET /clients/{clientId}
X-Admin-Key: <admin-key>
```

Response now shows `"hasAdoCredentials": true`, `"adoTenantId": "..."`, `"adoClientId": "..."`.

## 3. Add a crawl configuration — no credential change needed

```http
POST /clients/{clientId}/crawl-configurations
X-Client-Key: <client-key>
Content-Type: application/json

{
  "organizationUrl": "https://dev.azure.com/contoso",
  "projectId": "MyProject",
  "reviewerDisplayName": "PR Review Bot",
  "crawlIntervalSeconds": 60
}
```

The identity resolver automatically uses the client's credentials when looking up the reviewer identity in `https://dev.azure.com/contoso`. All subsequent PR crawl jobs for this project will authenticate as the client's service principal.

## 4. Rotate credentials

When the service principal secret expires, update without touching crawl configurations:

```http
PUT /clients/{clientId}/ado-credentials
X-Admin-Key: <admin-key>
Content-Type: application/json

{
  "tenantId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "clientId": "yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy",
  "secret":   "new-secret-value"
}
```

New jobs pick up the updated secret immediately. No restart required.

## 5. Revert to global identity

```http
DELETE /clients/{clientId}/ado-credentials
X-Admin-Key: <admin-key>
```

Response: `204 No Content`

The client's crawl jobs will now use the backend's global ADO identity (`AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_CLIENT_SECRET` env vars or `DefaultAzureCredential`).

## Environment variables (no changes)

No new environment variables. Per-client credentials are stored in the database, not in env vars.

The existing global identity env vars remain the fallback for clients without per-client credentials:

| Variable              | Purpose                          |
|-----------------------|----------------------------------|
| `AZURE_CLIENT_ID`     | Global service principal ID      |
| `AZURE_TENANT_ID`     | Global Azure tenant              |
| `AZURE_CLIENT_SECRET` | Global service principal secret  |
