# Feature Specification: Automated PR Review with Persistent Job Tracking

**Feature Branch**: `002-pr-review-persistence`
**Created**: 2026-03-08
**Status**: Draft
**Input**: User description: "A feature through which the user is able to add the backend user as a reviewer to a PR using azure devops ui (in PR view we add it as a reviewer). The backend periodically crawls open PRs and checks if it is assigned. If it finds a PR that it does not work on yet, it performs a review of that PR. We now need to remember which PRs we worked on. In that we exchange the inmemory database with a postgres DB. We keep an overview of which jobs are running and have run so far, so the backend does not forget anymore."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Trigger Automated Review via Reviewer Assignment (Priority: P1)

A developer opens a pull request in Azure DevOps and adds the Meister ProPR service account as a reviewer via the standard Azure DevOps PR UI. The backend detects this assignment during its next crawl cycle and automatically queues and executes a review of that PR. The developer does not need to interact with any separate interface — the assignment in Azure DevOps is the sole trigger.

**Why this priority**: This is the core user-facing interaction. Without the ability to trigger the review through the Azure DevOps UI, the entire feature has no entry point.

**Independent Test**: Can be fully tested by adding the service account as a reviewer on a real or test PR and confirming that a review job is created and completed with review comments posted back to the PR.

**Acceptance Scenarios**:

1. **Given** an open PR exists and the Meister ProPR service account has been added as a reviewer via the Azure DevOps UI, **When** the backend completes its next crawl cycle, **Then** a review job for that PR is created with status "Pending" and subsequently executed.
2. **Given** the backend has previously reviewed a PR, **When** the crawl runs again and the same PR is still open with the service account still listed as reviewer, **Then** no new review job is created for that PR (idempotency).
3. **Given** a PR review job is in progress, **When** the crawl cycle runs, **Then** the job is not restarted or duplicated.

---

### User Story 2 - View Job History and Status (Priority: P2)

An operator or developer can inspect a list of all review jobs — both currently running and previously completed — to understand what the backend has reviewed, what is in progress, and what failed. This overview survives service restarts and retains history across sessions.

**Why this priority**: Without persistent job tracking, there is no way to audit past reviews, diagnose failures, or confirm that the system is operating correctly. This is what replaces the ephemeral in-memory state.

**Independent Test**: Can be fully tested by querying the job list endpoint after the service restarts and confirming that jobs created before the restart are still visible with their correct statuses.

**Acceptance Scenarios**:

1. **Given** one or more review jobs have been processed, **When** the operator queries the job list, **Then** all jobs are returned with their PR reference, current status, timestamps, and any result or error summary.
2. **Given** the backend service is restarted, **When** the operator queries the job list, **Then** previously completed and failed jobs are still present with accurate state (data is not lost).
3. **Given** a job is currently in the "Processing" state, **When** the operator queries the job list, **Then** the job appears with status "Processing" and its start time.

---

### User Story 3 - Prevent Duplicate Review Jobs Across Restarts (Priority: P3)

After the service restarts, the backend does not re-review PRs it has already completed a review for. The persistent store acts as a memory that survives outages and deployments, so no PR receives duplicate review comments because of a restart.

**Why this priority**: This is a correctness and noise-reduction concern. Duplicate comments on a PR degrade the developer experience significantly.

**Independent Test**: Can be fully tested by completing a review job, restarting the service, triggering a new crawl cycle, and confirming no duplicate review is posted to the PR.

**Acceptance Scenarios**:

1. **Given** a PR has a completed review job in the persistent store, **When** the service restarts and the crawl runs, **Then** no new review job is created for that PR.
2. **Given** a PR had a review job that failed, **When** the service restarts, **Then** the system retries the failed job rather than silently dropping it.

---

### Edge Cases

- What happens when the persistent store is temporarily unavailable at startup? The service should refuse to start rather than fall back to an empty in-memory state, avoiding data loss or duplicate reviews.
- What happens when a PR is closed or abandoned while its review job is still "Processing"? The job should transition to "Failed" with a reason recorded.
- What happens when the crawl finds a PR that was assigned to the service account but then the reviewer assignment is removed before the job starts? The job can proceed or be cancelled; see assumption below.
- What happens if two crawl cycles run concurrently (e.g., delayed prior run overlapping)? The system must prevent two jobs from being created for the same PR iteration (idempotency key on PR + iteration).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The backend service MUST periodically crawl the configured Azure DevOps project for open pull requests where the Meister ProPR service account is listed as a reviewer.
- **FR-002**: When the crawler finds a PR assigned to it that has no existing review job record, the system MUST create a new review job for that PR and begin the review process.
- **FR-003**: The system MUST NOT create duplicate review jobs for the same PR (idempotency: one job per PR per review iteration).
- **FR-004**: All review job records (Pending, Processing, Completed, Failed) MUST be persisted in a durable data store that survives service restarts.
- **FR-005**: The system MUST expose a query capability (API endpoint) allowing operators to retrieve the list of all review jobs, including their status, associated PR reference, start time, completion time, and any result summary or error detail.
- **FR-006**: When the service starts, it MUST read existing job state from the persistent store to resume awareness of previously processed PRs.
- **FR-007**: A failed review job MUST record the failure reason so it can be diagnosed without access to live logs.
- **FR-008**: The crawl interval MUST be configurable without requiring a code change.
- **FR-009**: The system MUST prevent two concurrent crawl cycles from creating duplicate jobs for the same PR (concurrent-safe job creation).
- **FR-010**: The system MUST store client identities (client key + display name) in the durable database so that client registrations survive restarts and are not dependent on a static environment variable list.
- **FR-011**: Each registered client MUST be able to have one or more crawl configurations stored in the database, each specifying the Azure DevOps organisation, project, and the service account reviewer identity to monitor. Crawl targets are managed per client, not globally.

### Key Entities *(include if feature involves data)*

- **Client**: A registered API caller. Key attributes: unique identifier, client key (the secret used in `X-Client-Key`), display name, active flag, registration timestamp.
- **CrawlConfiguration**: A per-client Azure DevOps crawl target. Key attributes: unique identifier, owning client reference, organisation URL, project ID, reviewer identity ID (the service account's ADO Guid for that organisation), active flag.
- **ReviewJob**: Represents the lifecycle of a single automated review task for a specific PR. Key attributes: unique identifier, owning client reference, PR reference (repository + PR ID + iteration), status (Pending / Processing / Completed / Failed), creation timestamp, processing start timestamp, completion timestamp, result summary, failure reason.
- **PullRequest Reference**: A pointer to the Azure DevOps PR being reviewed — repository name, PR numeric ID, PR iteration number. Used as the idempotency key.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can trigger an automated review solely by adding the service account as a reviewer in the Azure DevOps UI — no additional steps required outside of Azure DevOps.
- **SC-002**: After a service restart, 100% of previously recorded review jobs are visible and accurately reflect their pre-restart state within 30 seconds of the service becoming healthy.
- **SC-003**: No PR receives more than one set of automated review comments per PR iteration, even across service restarts or concurrent crawl cycles.
- **SC-004**: The job history endpoint responds with the full job list in under 2 seconds for up to 10,000 stored jobs.
- **SC-005**: A failed review job provides sufficient detail (PR reference + failure reason) that an operator can diagnose the issue without consulting application logs.
- **SC-006**: An operator can add a new crawl target for a client without restarting or reconfiguring the service — the change takes effect on the next crawl cycle.

## Assumptions

- The Meister ProPR service account already exists in the Azure DevOps organization and can be added as a reviewer without additional provisioning steps in scope of this feature.
- "One review per PR iteration" is the intended idempotency boundary — if a PR is force-pushed (new iteration), a new review job is acceptable.
- If the reviewer assignment is removed from a PR before the review job begins, the review proceeds anyway (assignment was the trigger; withdrawal does not cancel already-queued work).
- The crawl interval default is 60 seconds; operators can override at the crawl configuration level or via a global environment variable default.
- The persistent store migration (replacing in-memory) is a behind-the-scenes change; no change to existing external API contracts is required beyond the new endpoints for client management, crawl configuration, and the global job-list.
- Client and crawl configuration management APIs are included in scope; the `MEISTER_CLIENT_KEYS` environment variable is superseded by the database-backed client registry for this feature.
