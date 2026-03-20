# Quickstart: React to Mentions in PR Comments

**Feature**: `009-react-to-mentions`  
**Date**: 2026-03-20

---

## Prerequisites

- .NET 10 SDK (`dotnet --version` → `10.x.x`)
- Docker Desktop (for PostgreSQL via `docker compose` or `podman compose`)
- An Azure DevOps organisation with at least one project and an active PR
- A service principal or PAT for the ADO project

---

## 1. Start the Dependencies

```bash
# From repo root — starts PostgreSQL 17 only (without the app)
docker compose up db -d
```

---

## 2. Apply Database Migrations

The new feature adds three tables. Run migrations before starting the app:

```bash
dotnet ef database update \
  --project src/MeisterProPR.Infrastructure \
  --startup-project src/MeisterProPR.Api
```

---

## 3. Configure Environment Variables

Add to your `src/MeisterProPR.Api/appsettings.Development.json` (or set as env vars):

```json
{
  "DB_CONNECTION_STRING": "Host=localhost;Port=5432;Database=meister;Username=meister;Password=meister",
  "MEISTER_ADMIN_KEY": "dev-admin-key",
  "MEISTER_CLIENT_KEYS": "dev-client-key",
  "AI_ENDPOINT": "https://<your-aoai-resource>.openai.azure.com/",
  "AI_DEPLOYMENT": "gpt-4o",
  "AZURE_CLIENT_ID": "<service-principal-client-id>",
  "AZURE_TENANT_ID": "<tenant-id>",
  "AZURE_CLIENT_SECRET": "<secret>",
  "MENTION_CRAWL_INTERVAL_SECONDS": "30"
}
```

> `MENTION_CRAWL_INTERVAL_SECONDS` — controls how often `MentionScanWorker` polls for new mentions (default: 60, minimum: 10).

---

## 4. Run the Application

```bash
dotnet run --project src/MeisterProPR.Api
```

Or with hot-reload:

```bash
dotnet watch --project src/MeisterProPR.Api
```

---

## 5. Trigger a Mention

1. Open an active PR in your ADO project.
2. Post a comment containing `@<reviewer display name>` followed by a question, e.g.:
   ```
   @MeisterProPR what is the risk level of this change?
   ```
3. Wait one scan cycle (`MENTION_CRAWL_INTERVAL_SECONDS`).
4. The bot posts a reply in the same thread.

---

## 6. Run All Tests

```bash
dotnet test
```

To run only the new feature tests:

```bash
dotnet test --filter "Category=MentionReply|Category=MentionScan"
```

---

## 7. Verify in Logs

Watch for structured log entries in the console output:

| Event | Message pattern |
|---|---|
| Scan cycle started | `MentionScanCycleStarted {ConfigId}` |
| PR skipped (no new comments) | `MentionPrSkipped {PrId} LastCommentAt={ts}` |
| Mention found | `MentionFound {PrId} ThreadId={id} CommentId={id}` |
| Job enqueued | `MentionReplyJobEnqueued {JobId}` |
| Reply posted | `MentionReplyPosted {JobId} ThreadId={id}` |
| Job failed | `MentionReplyJobFailed {JobId} Error={msg}` |

---

## 8. Stub Mode (No ADO calls)

Set `ADO_STUB_PR=true` to run with existing no-op ADO stubs. In this mode:
- `IActivePrFetcher` returns an empty list (no PRs discovered)
- `IAdoThreadReplier` logs the reply text instead of posting it

This allows running the workers without a live ADO connection.
