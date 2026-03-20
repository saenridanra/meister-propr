# Data Model: React to Mentions in PR Comments

**Feature**: `009-react-to-mentions`  
**Date**: 2026-03-20

---

## New Domain Entities

### `MentionReplyJob` (Domain Entity)

State machine: **`Pending` → `Processing` → `Completed` | `Failed`** (mirrors `ReviewJob`)

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | PK |
| `ClientId` | `Guid` | FK → `clients.id` |
| `OrganizationUrl` | `string` | ADO organisation URL |
| `ProjectId` | `string` | ADO project ID |
| `RepositoryId` | `string` | ADO repository ID |
| `PullRequestId` | `int` | ADO PR number |
| `ThreadId` | `int` | ADO thread ID containing the mention |
| `CommentId` | `int` | ADO comment ID of the mention comment |
| `MentionText` | `string` | Raw content of the mention comment |
| `Status` | `MentionJobStatus` | `Pending / Processing / Completed / Failed` |
| `CreatedAt` | `DateTimeOffset` | When the job was enqueued |
| `ProcessingStartedAt` | `DateTimeOffset?` | When processing began |
| `CompletedAt` | `DateTimeOffset?` | When the job finished (success or failure) |
| `ErrorMessage` | `string?` | Set on failure |

**Unique constraint**: `(ClientId, PullRequestId, ThreadId, CommentId)` — prevents duplicate jobs for the same mention comment.

---

### `MentionProjectScan` (Domain Entity)

One row per `CrawlConfiguration`. Tracks the project-level watermark for PR discovery.

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | PK |
| `CrawlConfigurationId` | `Guid` | FK → `crawl_configurations.id` (unique) |
| `LastScannedAt` | `DateTimeOffset` | Used as `minLastUpdateDate` in ADO PR list query |
| `UpdatedAt` | `DateTimeOffset` | Last time this record was updated |

**Unique constraint**: `(CrawlConfigurationId)` — one scan state per config.

---

### `MentionPrScan` (Domain Entity)

One row per PR within a crawl config scope. Tracks the per-PR comment watermark.

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | PK |
| `CrawlConfigurationId` | `Guid` | FK → `crawl_configurations.id` |
| `RepositoryId` | `string` | ADO repository ID |
| `PullRequestId` | `int` | ADO PR number |
| `LastCommentSeenAt` | `DateTimeOffset` | Latest comment timestamp observed; used to skip re-fetches |
| `UpdatedAt` | `DateTimeOffset` | Last time this record was updated |

**Unique constraint**: `(CrawlConfigurationId, RepositoryId, PullRequestId)` — one per PR per config.

---

## New Database Tables

### `mention_reply_jobs`

```sql
CREATE TABLE mention_reply_jobs (
    id                     UUID        PRIMARY KEY,
    client_id              UUID        NOT NULL REFERENCES clients(id),
    organization_url       TEXT        NOT NULL,
    project_id             TEXT        NOT NULL,
    repository_id          TEXT        NOT NULL,
    pull_request_id        INTEGER     NOT NULL,
    thread_id              INTEGER     NOT NULL,
    comment_id             INTEGER     NOT NULL,
    mention_text           TEXT        NOT NULL,
    status                 TEXT        NOT NULL DEFAULT 'Pending',
    created_at             TIMESTAMPTZ NOT NULL,
    processing_started_at  TIMESTAMPTZ,
    completed_at           TIMESTAMPTZ,
    error_message          TEXT,
    CONSTRAINT uq_mention_reply_jobs_mention
        UNIQUE (client_id, pull_request_id, thread_id, comment_id)
);
```

### `mention_project_scans`

```sql
CREATE TABLE mention_project_scans (
    id                       UUID        PRIMARY KEY,
    crawl_configuration_id   UUID        NOT NULL REFERENCES crawl_configurations(id),
    last_scanned_at          TIMESTAMPTZ NOT NULL,
    updated_at               TIMESTAMPTZ NOT NULL,
    CONSTRAINT uq_mention_project_scans_config
        UNIQUE (crawl_configuration_id)
);
```

### `mention_pr_scans`

```sql
CREATE TABLE mention_pr_scans (
    id                       UUID        PRIMARY KEY,
    crawl_configuration_id   UUID        NOT NULL REFERENCES crawl_configurations(id),
    repository_id            TEXT        NOT NULL,
    pull_request_id          INTEGER     NOT NULL,
    last_comment_seen_at     TIMESTAMPTZ NOT NULL,
    updated_at               TIMESTAMPTZ NOT NULL,
    CONSTRAINT uq_mention_pr_scans_pr
        UNIQUE (crawl_configuration_id, repository_id, pull_request_id)
);
```

---

## New Enum

### `MentionJobStatus`

```csharp
public enum MentionJobStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
```

Lives in `MeisterProPR.Domain/Enums/`.

---

## EF Core Configuration Notes

- All three records mapped via `IEntityTypeConfiguration<T>` classes in `Infrastructure/Data/Configurations/`
- Column naming: snake_case throughout (e.g. `crawl_configuration_id`, `pull_request_id`)
- Unique indexes via `HasIndex(...).IsUnique()` in the configuration class
- Foreign key to `crawl_configurations` cascade: `DeleteBehavior.Cascade` for `MentionProjectScan` and `MentionPrScan` (if config deleted, scan state is deleted too); `DeleteBehavior.Restrict` for `MentionReplyJob → clients` (must not silently delete job history)
- `status` stored as `TEXT` (enum-to-string conversion via `HasConversion<string>()`)

---

## Relationship Diagram

```
clients (existing)
  │ 1
  │ ∞
mention_reply_jobs  (status: Pending/Processing/Completed/Failed)

crawl_configurations (existing)
  │ 1
  ├── 1  mention_project_scans  (project-level LastScannedAt watermark)
  │
  └── ∞  mention_pr_scans       (per-PR LastCommentSeenAt watermark)
```

---

## No Changes to Existing Tables

- `clients` — unchanged
- `crawl_configurations` — unchanged
- `review_jobs` — unchanged
