# Feature Specification: Per-Client ADO Identity

**Feature Branch**: `003-client-ado-auth`
**Created**: 2026-03-09
**Status**: Draft
**Input**: User description: "A feature that realizes the secret as part of a client configuration (!not crawl, we do not want to repeat this for every project in an org!)"

## Overview

Each client registration represents one Azure DevOps organization. Currently the system uses a single, globally configured identity to access all ADO organizations it is connected to. This forces the backend's service principal to be registered in every customer's Azure tenant — mixing the operator's identity with the customers' resources.

This feature lets an administrator attach an ADO service principal (tenant ID, client ID, and client secret) directly to a client record. All projects (crawl configurations) that belong to that client automatically inherit those credentials. The global identity remains as a fallback for deployments that do not require per-client isolation.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Register a Client with Its Own ADO Identity (Priority: P1)

An administrator adds a new customer organization to the system. That customer has created a service principal inside their own Azure tenant and shared the credentials with the operator. The admin registers the client and supplies the three credential fields at the same time. From that point on, any PR review jobs triggered for that client's projects use the customer's own ADO identity — no action is needed on the individual crawl configurations.

**Why this priority**: This is the entire value of the feature. Without it, all other stories have no foundation.

**Independent Test**: Register a client with valid ADO credentials, then trigger a crawl for one of its projects. Verify that the PR fetch succeeds using the supplied credentials and that the global identity is not used.

**Acceptance Scenarios**:

1. **Given** a valid admin key and a new client body containing tenant ID, client ID, and client secret, **When** the admin calls the create-client endpoint, **Then** the client is persisted with those credentials and the response does not include the secret value.
2. **Given** an existing client without ADO credentials, **When** the admin calls the update-client endpoint with all three credential fields supplied, **Then** the credentials are stored and subsequent jobs for that client use them.
3. **Given** a client with ADO credentials registered, **When** the system processes a review job for any of that client's projects, **Then** the ADO calls are authenticated with the client's service principal, not the global identity.

---

### User Story 2 — Rotate or Remove ADO Credentials (Priority: P2)

A customer rotates their service principal secret (e.g., the old one expires). The administrator updates the client record with the new secret. All subsequent jobs use the rotated secret without touching any crawl configuration.

**Why this priority**: Credentials expire or are revoked. Without rotation support, any credential change requires re-registering the client, which is disruptive.

**Independent Test**: Update a client's credentials via the API and verify the next crawl job uses the new credentials.

**Acceptance Scenarios**:

1. **Given** a client with existing ADO credentials, **When** the admin sends an update with a new client secret, **Then** the stored secret is replaced and the old secret is no longer used.
2. **Given** a client with existing ADO credentials, **When** the admin sends an update that omits the ADO credential fields entirely, **Then** the existing credentials are preserved (partial update semantics).
3. **Given** a client with existing ADO credentials, **When** the admin explicitly clears all three credential fields (sends null/empty values), **Then** the credentials are removed and the system falls back to the global identity for that client's jobs.

---

### User Story 3 — Inspect a Client Without Exposing Secrets (Priority: P3)

An administrator retrieves a client record to verify its configuration. The response confirms whether ADO credentials are present but never reveals the secret value.

**Why this priority**: Observability is necessary for operations, but leaking secrets in API responses would be a security regression.

**Independent Test**: Fetch a client that has ADO credentials stored and confirm the response contains a masked/omitted secret field while tenant ID and client ID are visible.

**Acceptance Scenarios**:

1. **Given** a client with ADO credentials, **When** the admin calls the get-client endpoint, **Then** the response includes tenant ID and client ID but the secret is replaced with a placeholder (e.g., `"****"`) or omitted entirely.
2. **Given** a client without ADO credentials, **When** the admin calls the get-client endpoint, **Then** the ADO credential fields are absent or null in the response.

---

### Edge Cases

- What happens when the client has ADO credentials but they are invalid (wrong secret or revoked)? The job must fail with a clear authentication error rather than silently falling back to the global identity.
- What happens if only one or two of the three credential fields are supplied (e.g., tenant ID and client ID but no secret)? The system must reject the registration as incomplete — partial credentials are not usable.
- What happens to in-flight jobs when credentials are rotated? Jobs that have already started continue with the credential snapshot they obtained at dispatch time; only new jobs pick up the updated credentials.
- What happens when a client is deactivated and reactivated? Its stored credentials must survive the deactivation/reactivation cycle unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow an administrator to supply an ADO service principal (tenant ID, client ID, and client secret) as optional fields when creating or updating a client record.
- **FR-002**: The system MUST reject a create or update request that supplies some but not all three ADO credential fields (all three must be provided together or not at all).
- **FR-003**: When processing a review job, the system MUST use the owning client's ADO credentials if they are present, and fall back to the global configured identity only if the client has no credentials stored.
- **FR-004**: ADO credentials stored at the client level MUST apply to all crawl configurations that belong to that client, with no per-crawl credential override required.
- **FR-005**: The client secret MUST never be returned in any API response; the response MUST either omit the field or replace it with a fixed mask value.
- **FR-006**: The system MUST allow an administrator to clear all ADO credentials from a client (reverting it to global-identity mode) by explicitly sending null or empty values for all three fields simultaneously.
- **FR-007**: When ADO credentials are present but authentication fails (invalid or expired), the system MUST record the job as failed with an authentication error and MUST NOT silently retry with the global identity.
- **FR-008**: Credential updates MUST take effect for all jobs dispatched after the update; jobs already in progress are not affected.

### Key Entities

- **Client**: An organization-level registration. Extended with three optional ADO credential fields: ADO tenant ID, ADO client ID, and ADO client secret. The three fields form an atomic group — either all are present or none are.
- **Crawl Configuration**: Unchanged. Continues to hold the project-level ADO targeting (organization URL, project, reviewer). Inherits ADO identity from its parent client.
- **ADO Identity**: A logical concept grouping the three credential fields. Resolved at job dispatch time from the client record; absent credentials cause resolution to fall through to the global identity.

## Assumptions

- Client secret is stored as plaintext in the database for the initial implementation. The database is accessible only within the private container network. Envelope encryption or a secrets vault reference is deferred to a future feature.
- The existing admin-key-protected `/clients` endpoints are the only surface for managing credentials; no self-service credential management by the client API key holder is in scope.
- The three credential fields are always managed as a unit. There is no API for updating only one of the three.
- The global identity (configured via environment variables) continues to work exactly as today for clients that have no per-client credentials.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can register a client with ADO credentials and have the first PR review job complete successfully using those credentials, without any additional configuration steps.
- **SC-002**: All crawl configurations belonging to a client automatically use the client's ADO credentials — zero per-project credential configuration is required.
- **SC-003**: Rotating a client's secret requires a single API call; no crawl configuration changes and no service restart are needed for subsequent jobs to use the new secret.
- **SC-004**: A GET request for any client never returns the raw secret value, regardless of how the client was created or updated.
- **SC-005**: A review job that encounters an authentication failure due to invalid per-client credentials is recorded as failed within the same time budget as any other job failure — no additional latency from silent fallback attempts.
