# Research: React to Mentions in PR Comments

**Feature**: `009-react-to-mentions`  
**Date**: 2026-03-20

---

## 1. ADO SDK: Replying to an Existing Thread

**Decision**: Use `gitClient.CreateCommentAsync(comment, repositoryId, pullRequestId, threadId, projectId, ct)` to append a reply to a specific thread.

**Rationale**: The existing `AdoCommentPoster` only calls `gitClient.CreateThreadAsync(...)` which always creates a new top-level thread. ADO's Git client exposes a distinct `CreateCommentAsync` method that takes a `threadId` parameter and adds the new comment as a reply within that thread. This is the correct API call for FR-004 and FR-013.

**New `Comment` construction for a reply**:
```csharp
var reply = new Comment
{
    Content      = replyText,
    CommentType  = CommentType.Text,
    ParentCommentId = 0,  // 0 = top-level reply in thread, not nested
};
await gitClient.CreateCommentAsync(reply, repositoryId, prId, threadId, projectId, ct);
```

**Alternatives considered**: Extending `AdoCommentPoster.PostAsync` with a reply overload — rejected because it would mix review-posting concerns with mention-reply concerns in a single class. A dedicated `IAdoThreadReplier` interface keeps concerns separate and is independently mockable in tests.

---

## 2. ADO SDK: Project-Scoped PR Discovery with Date Filter

**Decision**: Use `gitClient.GetPullRequestsByProjectAsync(projectId, criteria, top: 200, ct)` with `GitPullRequestSearchCriteria { Status = PullRequestStatus.Active, MinTime = lastProjectScanAt }`.

**Rationale**: The existing `AdoAssignedPrFetcher` uses the same method but adds `ReviewerId` to the criteria. Removing the reviewer filter and adding `MinTime` gives us all recently-updated PRs in the project regardless of reviewer assignment (FR-002). The `MinTime` filter maps directly to `minLastUpdateDate` on the ADO REST API, which filters by the PR's `lastMergeSourceCommit.author.date` or the PR's own `creationDate` — practically, ADO updates `updatedDate` on any PR activity (new commits, comments, votes), so this correctly scopes to recently-active PRs.

**Alternatives considered**: See spec FR-002 clarifications.

---

## 3. Channel-Based Producer/Consumer Queue

**Decision**: `Channel<MentionReplyJob>.CreateBounded(capacity: 1000)` registered as a singleton in DI. `MentionScanWorker` writes to `_channel.Writer`; `MentionReplyWorker` reads from `_channel.Reader`.

**Rationale**: `System.Threading.Channels` is a .NET built-in (no NuGet dep). Bounded capacity (1000) provides backpressure — if the consumer falls behind, the producer waits rather than growing unbounded. This is more efficient than the existing DB-poll pattern (`ReviewJobWorker` polls every 2s) because the consumer wakes immediately when work arrives via the channel, rather than waking periodically to find nothing.

**Startup hydration**: In `MentionReplyWorker.StartAsync`, query `IMentionReplyJobRepository.GetPendingAsync()` and write each to the channel before the consumer loop starts. This recovers any `Pending` jobs that were in-flight when the service last shut down.

**Capacity justification**: 1000 is large enough to hold a full startup-hydration set without blocking, while small enough to avoid memory concerns. If the channel is full (bounded write blocks), `MentionScanWorker` simply waits — this is correct backpressure behavior.

**Alternatives considered**: `ConcurrentQueue<T>` (no backpressure, requires manual signalling); `BlockingCollection<T>` (legacy API, less idiomatic in .NET 10).

---

## 4. Mention Detection: VSTS Markup Parsing

**Decision**: Two-step detection (FR-012):
1. Parse `<at id="{reviewer-guid}">...</at>` in `Comment.Content` — GUID match against `VssConnection.AuthorizedIdentity.Id`
2. Fallback: case-insensitive substring match on `@{client.DisplayName}` in `Comment.Content`

**Rationale**: When a user types `@MeisterProPR` in the ADO web UI or extension, ADO emits the comment with VSTS mention markup: `<at id="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx">MeisterProPR</at>`. However, comments added via ADO REST API or older clients may contain the plain display name only. The two-step approach handles both cases reliably.

**Implementation**: A `MentionDetector` class in `Application/Services/` (pure logic, no ADO dependency — testable standalone):

```csharp
public static bool IsMentioned(string content, Guid reviewerGuid, string displayName)
{
    // Step 1: VSTS markup
    if (content.Contains($"id=\"{reviewerGuid}\"", StringComparison.OrdinalIgnoreCase))
        return true;
    // Step 2: plain-text fallback
    return content.Contains($"@{displayName}", StringComparison.OrdinalIgnoreCase);
}
```

**False-positive risk**: Display-name fallback could match a user mentioning a similarly-named person. Acceptable at current scale; GUID match is always authoritative.

---

## 5. AI Answer Service

**Decision**: New `IMentionAnswerService` Domain interface and `AgentMentionAnswerService` Infrastructure implementation mirroring `IAiReviewCore` / `AgentAiReviewCore`.

**Rationale**: `IAiReviewCore` is a Domain interface (`MeisterProPR.Domain/Interfaces/`) with a single-method signature `ReviewAsync(PullRequest, ct) → ReviewResult`. The same pattern is used for the answer service: `AnswerAsync(PullRequest pullRequest, string question, CancellationToken ct) → string`. This keeps the AI seam in the Domain layer (swappable) and the `IChatClient` binding in Infrastructure only.

**System prompt strategy**: Return a short, direct, markdown-formatted answer to the specific question asked, grounded in the PR diff and description. NOT a full review. Prompt signals: role = "PR assistant", task = "answer one question", context = PR title + description + relevant file diffs + existing threads. Max ~500 tokens for the response.

---

## 6. EF Core: New Tables

**Decision**: Two new EF entities (`MentionProjectScanRecord`, `MentionPrScanRecord`) for watermark tracking, and one new entity (`MentionReplyJobRecord`) for the durable queue. All mapped to PostgreSQL via existing `ApplyConfigurationsFromAssembly` pattern.

**Column naming convention** (from existing migrations): all snake_case, UUIDs as `uuid`, strings as `text`, timestamps as `timestamptz`.

**Tables**:
- `mention_project_scans` — one row per `CrawlConfiguration`, stores `last_scanned_at`
- `mention_pr_scans` — one row per PR within a crawl config scope, stores `last_comment_seen_at`
- `mention_reply_jobs` — durable queue, state machine `Pending → Processing → Completed | Failed`

**Alternatives considered**: Storing project-level watermarks on `CrawlConfiguration` directly — rejected because it mixes scan-state concerns into client configuration records.

---

## 7. New Interfaces Summary

| Interface | Layer | Description |
|---|---|---|
| `IActivePrFetcher` | Application | Fetch recently-updated active PRs by project, with date filter |
| `IAdoThreadReplier` | Application | Post a reply into an existing ADO PR thread |
| `IMentionScanRepository` | Application | CRUD for `MentionProjectScan` and `MentionPrScan` records |
| `IMentionReplyJobRepository` | Application | CRUD + status transitions for `MentionReplyJob` records |
| `IMentionAnswerService` | Domain | Generate an AI answer to a mention question given PR context |

---

## 8. No New REST Endpoints

This feature is entirely background-worker driven. No new controllers, no changes to `openapi.json`. Constitution Principle I is trivially satisfied.
