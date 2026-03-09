# Data Model: PR Review Auto-Assignment & PostgreSQL Persistence

**Feature**: `002-pr-review-persistence`
**Date**: 2026-03-08

---

## Entities Overview

```
clients ─────────────────────┐
    │                        │
    │ 1:N                    │ 1:N
    ▼                        ▼
crawl_configurations      review_jobs
```

---

## Entity: Client

Represents a registered API consumer. Replaces the `MEISTER_CLIENT_KEYS` environment-variable list.

**Table**: `clients`

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| `id` | UUID | PK | `gen_random_uuid()` default |
| `key` | TEXT | NOT NULL, UNIQUE | The secret used in `X-Client-Key` header |
| `display_name` | TEXT | NOT NULL | Human-readable name for dashboards |
| `is_active` | BOOL | NOT NULL, DEFAULT true | Soft-delete / disable without data loss |
| `created_at` | TIMESTAMPTZ | NOT NULL, DEFAULT now() | |

**Indexes**:
- `ix_clients_key` — UNIQUE on `key` (supports fast O(1) lookup during request validation)

**State transitions**: `is_active = true` ↔ `is_active = false` (toggle only; no deletion)

**Validation rules**:
- `key` must be non-empty; minimum 16 characters recommended at application level
- `display_name` must be non-empty

---

## Entity: CrawlConfiguration

A per-client Azure DevOps crawl target. Tells the crawler which organisation/project to monitor and which ADO identity is the service-account reviewer.

**Table**: `crawl_configurations`

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| `id` | UUID | PK | `gen_random_uuid()` default |
| `client_id` | UUID | NOT NULL, FK → clients.id | Owning client |
| `organization_url` | TEXT | NOT NULL | e.g. `https://dev.azure.com/myorg` |
| `project_id` | TEXT | NOT NULL | ADO project ID or name |
| `reviewer_id` | UUID | NOT NULL | ADO identity GUID of the service account reviewer |
| `crawl_interval_seconds` | INT | NOT NULL, DEFAULT 60 | Per-target override; minimum 10 |
| `is_active` | BOOL | NOT NULL, DEFAULT true | Pause crawl without deleting config |
| `created_at` | TIMESTAMPTZ | NOT NULL, DEFAULT now() | |

**Indexes**:
- `ix_crawl_configurations_client_id` — on `client_id`
- `ix_crawl_configurations_active` — partial index on `is_active = true` (fast load of active configs for crawler)

**Validation rules**:
- `organization_url` must be a valid HTTPS URL
- `reviewer_id` must be a non-empty GUID
- `crawl_interval_seconds` must be ≥ 10

---

## Entity: ReviewJob

Represents the full lifecycle of one automated review task. Replaces the `ConcurrentDictionary` in `InMemoryJobRepository`.

**Table**: `review_jobs`

| Column | Type | Constraints | Notes |
|--------|------|-------------|-------|
| `id` | UUID | PK | Assigned by application on creation |
| `client_key` | TEXT | NULL | Null for crawler-initiated jobs; non-null for API-initiated jobs |
| `organization_url` | TEXT | NOT NULL | |
| `project_id` | TEXT | NOT NULL | |
| `repository_id` | TEXT | NOT NULL | |
| `pull_request_id` | INT | NOT NULL | |
| `iteration_id` | INT | NOT NULL | PR iteration (incremented on force-push) |
| `status` | TEXT | NOT NULL | CHECK in ('Pending','Processing','Completed','Failed') |
| `submitted_at` | TIMESTAMPTZ | NOT NULL | |
| `processing_started_at` | TIMESTAMPTZ | NULL | Set when job transitions Pending → Processing |
| `completed_at` | TIMESTAMPTZ | NULL | Set on Completed or Failed transition |
| `result_summary` | TEXT | NULL | Human-readable review summary |
| `result_comments` | JSONB | NULL | Serialised `ReviewComment[]` array |
| `error_message` | TEXT | NULL | Set on Failed transition |

**Indexes**:
- `ix_review_jobs_status` — on `status` (supports `GetPendingJobs`, `GetProcessingJobs`, `GetAllJobs` queries)
- `ix_review_jobs_client_key` — on `client_key` (supports `GetAllForClient` query)
- `ix_review_jobs_pr_identity` — on `(organization_url, project_id, repository_id, pull_request_id, iteration_id)` (supports `FindActiveJob` lookup)

**State machine**:
```
Pending ──► Processing ──► Completed
                      └──► Failed
```
- Only `Pending → Processing` and `Processing → Completed|Failed` transitions are valid
- `TryTransition` uses optimistic concurrency (EF Core row version or compare-then-update)

**Idempotency rule**: `FindActiveJob` returns the first job for the given PR identity where `status != 'Failed'`. The crawler and POST /reviews endpoint both check this before creating a new job.

---

## Value Object: ReviewComment (JSONB column schema)

Stored as an element in the `result_comments` JSONB array on `review_jobs`.

| Field | JSON key | Type | Notes |
|-------|----------|------|-------|
| `FilePath` | `filePath` | string? | Null for PR-level comments |
| `LineNumber` | `lineNumber` | int? | Null for file-level or PR-level comments |
| `Severity` | `severity` | string | "Info" / "Warning" / "Error" / "Suggestion" |
| `Message` | `message` | string | The review comment text |

---

## EF Core Mapping Notes

- All entities mapped via `IEntityTypeConfiguration<T>` Fluent API — zero EF attributes on domain classes
- `ReviewJob.Result` (`ReviewResult` value object) mapped via `OwnsOne(...).ToJson()` which stores `result_summary` and `result_comments` as part of the JSONB representation — OR mapped as two separate columns (preferred for queryability); see Infrastructure implementation for final mapping choice
- `Client` and `CrawlConfiguration` are **new** EF entities with no corresponding domain entity initially; they are read/written directly as EF models in Infrastructure
- `DbContext`: `MeisterProPRDbContext` lives in `src/MeisterProPR.Infrastructure/Data/`
- Migrations: `src/MeisterProPR.Infrastructure/Data/Migrations/`

---

## Bootstrap / Seed Logic

On first startup, if `clients` table is empty AND `MEISTER_CLIENT_KEYS` environment variable is set:
- Parse the comma-separated keys
- Insert each as a `Client` record with `display_name = "Bootstrapped from MEISTER_CLIENT_KEYS"` and `is_active = true`
- Log a warning that the env var is superseded and will be ignored in future startups

This ensures zero-downtime migration from the env-var-based client registry.
