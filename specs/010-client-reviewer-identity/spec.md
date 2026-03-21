# Feature Specification: Client Self-Managed Reviewer Identity

**Feature Branch**: `010-client-reviewer-identity`
**Created**: 2026-03-21
**Status**: Approved

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Client Sets Its Own Reviewer Identity (Priority: P1)

A client (e.g. the Azure DevOps settings extension acting on behalf of a team) wants to configure which identity the AI reviewer will post comments under in Azure DevOps. Today, only a platform administrator can make this change. The client needs to be able to do this itself using only its own API key, without requesting admin intervention.

**Why this priority**: This is the core capability the feature unlocks. Without it, the Azure DevOps settings extension cannot function as a self-service configuration tool, and every reviewer identity change requires an out-of-band admin operation.

**Independent Test**: Can be fully tested by issuing a reviewer identity update request authenticated with a client key and confirming the identity is stored and used on the next review job.

**Acceptance Scenarios**:

1. **Given** an authenticated client with a valid client key, **When** the client submits a valid reviewer identity, **Then** the system stores the identity and returns a success response.
2. **Given** an authenticated client with a valid client key, **When** the client submits a reviewer identity that is already set to the same value, **Then** the system accepts the request idempotently and returns a success response.
3. **Given** an unauthenticated request (missing client key), **When** a reviewer identity update is submitted, **Then** the system rejects the request with an authentication error.
4. **Given** an authenticated client with a valid client key, **When** the client submits a reviewer identity for a different client's record, **Then** the system rejects the request with an authorisation error.
5. **Given** an authenticated client with a valid client key, **When** the client submits an invalid or empty reviewer identity, **Then** the system rejects the request with a validation error.

---

### User Story 2 - Client Reads Its Own Reviewer Identity (Priority: P2)

The Azure DevOps settings extension needs to display the currently configured reviewer identity to the user so they can verify or update it. The client must be able to retrieve its own reviewer identity without requiring admin credentials.

**Why this priority**: Required for the settings extension to provide a read-before-write flow and to show current state. Without read access, the extension would have to blindly overwrite the value.

**Independent Test**: Can be fully tested by retrieving client details using a client key and confirming the reviewer identity field is included in the response.

**Acceptance Scenarios**:

1. **Given** an authenticated client with a valid client key, **When** the client requests its own details, **Then** the response includes the currently stored reviewer identity (or indicates none is set).
2. **Given** an authenticated client with a valid client key, **When** the client requests details for a different client, **Then** the system rejects the request with an authorisation error.

---

### User Story 3 - Admin Retains Reviewer Identity Management (Priority: P3)

Platform administrators must continue to be able to set and manage the reviewer identity for any client using admin credentials, as they do today.

**Why this priority**: Backwards compatibility — existing integrations and admin workflows must not break.

**Independent Test**: Can be fully tested by issuing a reviewer identity update via admin credentials and verifying it is stored correctly, independently of any client key flow.

**Acceptance Scenarios**:

1. **Given** a valid admin key, **When** an admin sets a reviewer identity for any client, **Then** the system stores the identity and returns success.
2. **Given** an invalid or missing admin key, **When** an admin reviewer identity update is attempted, **Then** the system rejects the request with an authentication error.

---

### Edge Cases

- What happens when a client updates its reviewer identity while a review job is already in progress? (The in-flight job uses the identity resolved at job start; the new identity applies to subsequent jobs.)
- What happens when the reviewer identity is set to a GUID that is not recognised by Azure DevOps? (The system stores whatever valid non-empty GUID is provided; ADO validation happens at job execution time — the job will fail gracefully if the identity is not found.)
- What happens if the identity resolution lookup returns multiple matches for a display name? (Identity resolution is performed by the Azure DevOps Extension SDK in the settings extension, not by the backend. The extension is responsible for selecting the correct GUID before submitting it for storage.)

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow an authenticated client to set or replace its own reviewer identity using only its own client credentials.
- **FR-002**: The system MUST reject reviewer identity updates from a client attempting to modify another client's record.
- **FR-003**: The system MUST allow an authenticated client to read its own current reviewer identity as part of its client profile.
- **FR-004**: The system MUST continue to allow an administrator to set the reviewer identity for any client using admin credentials (no regression).
- **FR-005**: The system MUST reject reviewer identity submissions where the identity value is absent or invalid (e.g. an empty/zero GUID).
- **FR-006**: The system MUST treat setting the reviewer identity to its current value as a successful no-op (idempotent).
- **FR-007**: The system MUST record an audit-friendly log entry whenever a reviewer identity is changed, including the client identifier and the actor type (client self-service vs. admin).

### Key Entities

- **Client**: Represents a registered consumer of the meister-propr service. Carries zero or one reviewer identity. The reviewer identity determines which Azure DevOps account posts AI review comments.
- **Reviewer Identity**: An opaque, globally unique identifier that references a specific user or service account in Azure DevOps. Stored per-client; used at review job execution time.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A client can update its own reviewer identity in under 3 seconds end to end.
- **SC-002**: Zero admin interventions are required for a client to configure or update its own reviewer identity after initial client registration.
- **SC-003**: The Azure DevOps settings extension can complete a full read-modify-write cycle for reviewer identity using only the client's own credentials, with no admin key required.
- **SC-004**: 100% of existing admin-managed reviewer identity operations continue to succeed without modification (no regression).
- **SC-005**: All unauthorised attempts to modify another client's reviewer identity are rejected with an authorisation error, verified by automated tests.

## Assumptions

- The Azure DevOps settings extension already holds a valid client key for the client it manages; it does not possess an admin key.
- Identity resolution (mapping a display name to a GUID) is performed client-side by the Azure DevOps Extension SDK within the settings extension. The backend only stores and retrieves the resolved GUID; it does not provide a resolution endpoint.
- A client may update its reviewer identity any number of times; there is no approval workflow or change-request gate.
- The reviewer identity is a single value per client (not per crawl configuration or per project).
