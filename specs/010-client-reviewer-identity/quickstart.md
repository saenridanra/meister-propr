# Quickstart: Client Self-Managed Reviewer Identity

**Feature**: 010-client-reviewer-identity

---

## Prerequisites

- A running meister-propr instance (local or Docker)
- A registered client with a valid `X-Client-Key`
- The client's `clientId` GUID (returned when the client was created)
- The Azure DevOps Extension SDK (available in the settings extension context)

---

## Typical self-service flow

### Step 1: Resolve the reviewer identity GUID

Identity resolution is performed **client-side within the Azure DevOps settings extension** using the ADO Extension SDK identity picker. The backend does not provide a resolution endpoint.

```typescript
// Example using the ADO Extension SDK identity picker
import * as VSSService from "VSS/Service";
import * as IdentityPickerService from "VSS/Identities/Picker/Services";

const identityService = await VSSService.getServiceContribution<IdentityPickerService.IIdentityPickerService>(
    IdentityPickerService.ServiceHelpers.IdentityPickerServiceContributionId
);

const result = await identityService.getIdentities({
    identityType: { User: true, Group: true, ServiceIdentity: true },
    operationScope: { IMS: true, Source: true },
    queryTypeHint: { UID: true },
    filterByScope: { ProjectId: context.project.id },
});
```

Copy the `localId` (VSS identity GUID) from the selected identity.

### Step 2: Read current reviewer identity

Verify the currently stored value (or confirm it is not set yet):

```http
GET /clients/{clientId}/profile
X-Client-Key: <your-client-key>
```

Response:

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "displayName": "My Team",
  "isActive": true,
  "createdAt": "2026-03-10T09:15:00Z",
  "reviewerId": null
}
```

`reviewerId: null` means no reviewer identity is configured yet.

### Step 3: Set the reviewer identity

```http
PUT /clients/{clientId}/reviewer-identity
X-Client-Key: <your-client-key>
Content-Type: application/json

{
  "reviewerId": "7c9e6679-7425-40de-944b-e07fc1f90ae7"
}
```

Success: `204 No Content`. The identity is now stored and will be used on the next review job.

### Step 4: Confirm the change

```http
GET /clients/{clientId}/profile
X-Client-Key: <your-client-key>
```

Response:

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "displayName": "My Team",
  "isActive": true,
  "createdAt": "2026-03-10T09:15:00Z",
  "reviewerId": "7c9e6679-7425-40de-944b-e07fc1f90ae7"
}
```

---

## Admin flow (unchanged)

Admins can continue to use `X-Admin-Key` to set the reviewer identity for any client:

```http
PUT /clients/{clientId}/reviewer-identity
X-Admin-Key: <admin-key>
Content-Type: application/json

{
  "reviewerId": "7c9e6679-7425-40de-944b-e07fc1f90ae7"
}
```

---

## Error reference

| Response | Meaning                                                              |
|----------|----------------------------------------------------------------------|
| 400      | `reviewerId` is missing or is the zero GUID (`00000000-...`)         |
| 401      | No valid `X-Client-Key` or `X-Admin-Key` header provided             |
| 403      | Client key provided but belongs to a different client                |
| 404      | `clientId` not found                                                 |
