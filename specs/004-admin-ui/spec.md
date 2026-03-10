# Feature Specification: Admin Management UI

**Feature Branch**: `004-admin-ui`
**Created**: 2026-03-09
**Status**: Draft
**Input**: User description: "A new management interface for the backend application in which clients can fully be managed (created, updated, deleted). The login is done using the admin key for now."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Log In to the Admin Interface (Priority: P1)

An administrator opens the management interface in a browser and enters the admin key to access it. After entering the correct key, the dashboard is shown. If the key is wrong, an error message is displayed and access is denied.

**Why this priority**: Without authentication, no other management action can be performed. This is the entry point for all admin workflows.

**Independent Test**: Can be tested by navigating to the interface, submitting a valid admin key, and confirming access to the client list is granted; submitting an invalid key must be denied.

**Acceptance Scenarios**:

1. **Given** the interface is open and no session is active, **When** the administrator enters the correct admin key and submits, **Then** they are taken to the client list dashboard.
2. **Given** the interface is open, **When** an incorrect admin key is submitted, **Then** an error is shown and the administrator remains on the login screen.
3. **Given** an authenticated session, **When** the administrator logs out, **Then** the session is cleared and the login screen is shown again.

---

### User Story 2 — View and Search Clients (Priority: P2)

An authenticated administrator sees a list of all registered clients with their display name, active/inactive status, and whether they have per-client ADO credentials configured. The list is searchable.

**Why this priority**: Viewing existing clients is necessary before any create, update, or delete action and provides ongoing operational visibility.

**Independent Test**: Can be tested by logging in and verifying the client list renders with correct columns and reflects the current state of the backend.

**Acceptance Scenarios**:

1. **Given** at least one client exists, **When** the administrator views the client list, **Then** each entry shows display name, active status, and whether ADO credentials are configured.
2. **Given** multiple clients, **When** the administrator types in the search field, **Then** the list filters in real-time to matching entries.
3. **Given** no clients exist, **When** the administrator views the list, **Then** a friendly empty-state message is displayed.

---

### User Story 3 — Register a New Client (Priority: P2)

The administrator fills in a form to create a new client by providing a display name and a client key. The new client appears immediately in the list after creation.

**Why this priority**: Creating clients is the primary write operation and directly enables access to the backend API for new consumers.

**Independent Test**: Can be tested independently by submitting the create-client form and verifying the new client appears in the list.

**Acceptance Scenarios**:

1. **Given** the create-client form is open, **When** a valid display name and key are submitted, **Then** the client is created and appears in the list as active.
2. **Given** the create-client form, **When** a required field is left blank, **Then** a validation error is shown inline and the form is not submitted.
3. **Given** a key that is already in use, **When** the administrator submits the form, **Then** an error is shown indicating the key is not available.

---

### User Story 4 — Edit a Client (Priority: P3)

The administrator can update a client's display name, toggle its active/inactive status, and manage per-client ADO credentials (set or clear them) from a dedicated detail/edit view.

**Why this priority**: Ongoing maintenance of clients is needed but less immediately critical than creation and viewing.

**Independent Test**: Can be tested by selecting an existing client, changing its display name, toggling its status, and verifying the changes are reflected in the list.

**Acceptance Scenarios**:

1. **Given** a client detail view, **When** the administrator changes the display name and saves, **Then** the updated name is shown in the list.
2. **Given** a client that is active, **When** the administrator disables it, **Then** the client becomes inactive and the status indicator updates.
3. **Given** a client with no ADO credentials, **When** the administrator enters a tenant ID, client ID, and secret and saves, **Then** the credentials are stored and the UI shows that ADO credentials are configured.
4. **Given** a client with ADO credentials, **When** the administrator clears them, **Then** the credentials are removed and the UI reflects that no credentials are configured.

---

### User Story 5 — Delete a Client (Priority: P3)

The administrator selects a client and chooses to delete it. A confirmation prompt prevents accidental deletion. After confirmation the client is removed and no longer appears in the list.

**Why this priority**: Deletion is a destructive action needed for lifecycle management but is lower priority than create/edit and requires an extra confirmation step.

**Independent Test**: Can be tested by deleting an existing client, confirming the dialog, and verifying the client is gone from the list.

**Acceptance Scenarios**:

1. **Given** a client in the list, **When** the administrator clicks "Delete" and then cancels the confirmation, **Then** the client is not deleted.
2. **Given** a client, **When** the administrator confirms deletion, **Then** the client is permanently removed and is no longer shown in the list.

---

### Edge Cases

- What happens when the backend is unreachable? The interface shows a clear error and does not lose the current view state.
- What happens when the admin key is revoked mid-session? The interface detects the 401 response, clears the session, and redirects to the login screen.
- What if two admins simultaneously delete the same client? The second delete receives a "not found" error and the interface shows an appropriate message.
- What if a client's key conflicts with an existing one on creation? The form displays the conflict error inline without losing other field values.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST present a login screen that accepts an admin key before granting access to any management feature.
- **FR-002**: System MUST reject access and display a clear error when an incorrect admin key is submitted.
- **FR-003**: System MUST display a list of all registered clients showing display name, active status, and ADO credential status.
- **FR-004**: System MUST allow filtering the client list by display name in real-time.
- **FR-005**: System MUST provide a form to create a new client with display name and client key fields, with inline validation.
- **FR-006**: System MUST allow the administrator to update a client's display name.
- **FR-007**: System MUST allow the administrator to enable or disable a client.
- **FR-008**: System MUST allow the administrator to set per-client ADO credentials (tenant ID, client ID, secret).
- **FR-009**: System MUST allow the administrator to clear per-client ADO credentials.
- **FR-010**: System MUST never display the ADO client secret — only whether credentials are configured.
- **FR-011**: System MUST allow the administrator to delete a client after confirming the action in a dialog.
- **FR-012**: System MUST surface user-friendly error messages when backend operations fail (network errors, validation errors, conflicts).
- **FR-013**: System MUST allow the administrator to log out, clearing the stored session.
- **FR-014**: System MUST redirect to the login screen when it detects an authentication failure response from the backend.

### Key Entities

- **Client**: A consumer of the backend API identified by a unique key and display name; has an active/inactive state and optionally has ADO credentials associated.
- **ADO Credentials**: Per-client Azure service principal configuration (tenant ID, client ID, secret); the secret is write-only and never returned to the UI.
- **Admin Session**: A short-lived credential stored in the browser that carries the admin key for subsequent requests; cleared on logout or authentication failure.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can register a new client in under 60 seconds from opening the create form to seeing the client appear in the list.
- **SC-002**: The client list loads and is interactive within 2 seconds for up to 500 registered clients.
- **SC-003**: All management actions (create, disable, delete, set credentials) are reachable within 2 clicks from the client list.
- **SC-004**: An incorrect admin key is rejected within 1 second with a visible error message.
- **SC-005**: The ADO secret is never displayed anywhere in the UI — only a "credentials configured" indicator is shown.
- **SC-006**: All destructive actions (delete, clear credentials) require explicit confirmation before taking effect.

## Clarifications

### Session 2026-03-09

- Q: Which OpenAPI TypeScript client generator should be used for the Vue SPA? → A: `openapi-typescript` + `openapi-fetch` — generates pure TypeScript types from `openapi.json` at build time; `openapi-fetch` provides a typed, zero-runtime-overhead fetch wrapper around those types.

## Assumptions

- The admin interface is a lightweight single-page web application served as static files.
- The admin key is entered manually each session and stored in browser session storage (not persisted across browser restarts).
- No role hierarchy is required at this stage — any holder of the admin key has full access to all management operations.
- The existing backend REST API (`/clients`, `/clients/{id}/ado-credentials`, etc.) is used as-is; no new backend endpoints are introduced by this feature.
- The SPA API client is generated from the committed `openapi.json` at the repository root using `openapi-typescript` (type generation) and `openapi-fetch` (typed fetch wrapper); no hand-written HTTP fetch wrapper is maintained.
- The interface targets modern desktop browsers; mobile optimisation is a nice-to-have.
- No pagination is required initially; the full client list is fetched and filtered client-side.
