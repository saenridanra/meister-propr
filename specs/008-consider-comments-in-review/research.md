# Research: Feature 008 — Consider Existing PR Comments in Review

## ADO SDK: Fetching Comment Threads

**API**: `GitHttpClient.GetThreadsAsync(project, repositoryId, pullRequestId, iteration?, baseIteration?, userState?, ct?)`

- Returns `IEnumerable<GitPullRequestCommentThread>`
- Each thread has:
  - `Id` (int) — thread identifier
  - `ThreadContext` — contains `FilePath`, `RightFileStart.Line`, `LeftFileStart.Line`
  - `Comments` — `IList<Comment>` with `Author.DisplayName`, `Content`, `IsDeleted`
  - `IsDeleted` (bool?) — true for soft-deleted threads
  - `Status` — `CommentThreadStatus` (Active, Fixed, WontFix, Closed, ByDesign, Pending, Unknown)

**Passing `iteration = null`** returns all threads across all iterations (what we want for deduplication).

## Bot Identity Detection

We avoid querying the identity service. Instead, rely on the fact that bot comments always start with a known prefix:

| Comment type | Prefix |
|---|---|
| PR summary | `**AI Review Summary**` |
| Inline | `ERROR: `, `WARNING: `, `SUGGESTION: `, `INFO: ` |

This is reliable because:
1. The bot is the only entity using these exact prefixes
2. It requires no configuration or identity lookup
3. It works even if the service account changes

## Architecture Decision: Where to Fetch Threads

**Option A**: Fetch in `AdoPullRequestFetcher`, attach to `PullRequest` domain object
- Pros: Single ADO connection; threads flow naturally through the system
- Cons: `PullRequest` gains a new optional field

**Option B**: New `IAdoPrThreadFetcher` interface, injected into `ReviewOrchestrationService`
- Pros: Separation of concerns, no domain object change
- Cons: New interface, new ADO connection, orchestration service complexity

**Decision: Option A** — simpler, fewer moving parts, consistent with how `ChangedFiles` works. Adding an optional `ExistingThreads` parameter (default null) to `PullRequest` maintains backward compatibility with all existing tests.

## Architecture Decision: Passing Threads to Poster

`IAdoCommentPoster.PostAsync` needs the existing threads to deduplicate. Options:
- Pass `pr.ExistingThreads` as an optional parameter (chosen)
- Have `AdoCommentPoster` re-fetch threads internally (extra ADO call)

Chosen: add `IReadOnlyList<PrCommentThread>? existingThreads = null` to `PostAsync`. Default null = no deduplication (backward compatible).

## AI Prompt Strategy

Existing threads section appended after changed files:

```
## Existing Review Threads
These threads already exist on this PR. Consider them: avoid re-flagging resolved issues.

### Thread at /src/Foo.cs:L42
  [Bot]: ERROR: Null ref here.
  [Alice]: Fixed — added null check.

### Thread (PR-level)
  [Bot]: **AI Review Summary**\n\nLooks good overall.
```

Threads where only the bot has commented (no developer reply) are still included — they provide context on previously flagged issues.
