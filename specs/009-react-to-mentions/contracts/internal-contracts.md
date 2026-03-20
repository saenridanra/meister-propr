# Internal Contracts: React to Mentions

**Feature**: `009-react-to-mentions`  
**Date**: 2026-03-20  
**Note**: This feature introduces no new public REST endpoints. All contracts here are internal service/interface contracts between layers.

---

## `IActivePrFetcher` (Application → Infrastructure)

```csharp
// MeisterProPR.Application/Interfaces/IActivePrFetcher.cs
public interface IActivePrFetcher
{
    /// <summary>
    /// Fetches all active pull requests in a project that were updated
    /// at or after <paramref name="updatedAfter"/>.
    /// </summary>
    Task<IReadOnlyList<ActivePullRequestRef>> GetRecentlyUpdatedPullRequestsAsync(
        string organizationUrl,
        string projectId,
        DateTimeOffset updatedAfter,
        string? clientId = null,
        CancellationToken cancellationToken = default);
}

// MeisterProPR.Domain/ValueObjects/ActivePullRequestRef.cs
public sealed record ActivePullRequestRef(
    string OrganizationUrl,
    string ProjectId,
    string RepositoryId,
    int    PullRequestId,
    DateTimeOffset LastUpdatedAt
);
```

---

## `IAdoThreadReplier` (Application → Infrastructure)

```csharp
// MeisterProPR.Application/Interfaces/IAdoThreadReplier.cs
public interface IAdoThreadReplier
{
    /// <summary>
    /// Posts a reply comment into an existing pull request thread.
    /// </summary>
    Task ReplyAsync(
        string organizationUrl,
        string projectId,
        string repositoryId,
        int    pullRequestId,
        int    threadId,
        string replyText,
        string? clientId = null,
        CancellationToken cancellationToken = default);
}
```

---

## `IMentionAnswerService` (Domain interface)

```csharp
// MeisterProPR.Domain/Interfaces/IMentionAnswerService.cs
public interface IMentionAnswerService
{
    /// <summary>
    /// Generates an answer to a question asked in a PR comment mention,
    /// grounded in the PR's code diff, description, and existing threads.
    /// </summary>
    Task<string> AnswerAsync(
        PullRequest pullRequest,
        string question,
        CancellationToken cancellationToken = default);
}
```

---

## `IMentionScanRepository` (Application → Infrastructure)

```csharp
// MeisterProPR.Application/Interfaces/IMentionScanRepository.cs
public interface IMentionScanRepository
{
    // --- Project-level watermark ---
    Task<MentionProjectScan?> GetProjectScanAsync(
        Guid crawlConfigurationId,
        CancellationToken ct = default);

    Task UpsertProjectScanAsync(
        MentionProjectScan record,
        CancellationToken ct = default);

    // --- PR-level watermark ---
    Task<MentionPrScan?> GetPrScanAsync(
        Guid crawlConfigurationId,
        string repositoryId,
        int pullRequestId,
        CancellationToken ct = default);

    Task UpsertPrScanAsync(
        MentionPrScan record,
        CancellationToken ct = default);
}
```

---

## `IMentionReplyJobRepository` (Application → Infrastructure)

```csharp
// MeisterProPR.Application/Interfaces/IMentionReplyJobRepository.cs
public interface IMentionReplyJobRepository
{
    Task AddAsync(MentionReplyJob job, CancellationToken ct = default);

    Task<IReadOnlyList<MentionReplyJob>> GetPendingAsync(CancellationToken ct = default);

    Task<bool> ExistsForCommentAsync(
        Guid clientId,
        int pullRequestId,
        int threadId,
        int commentId,
        CancellationToken ct = default);

    Task<bool> TryTransitionAsync(
        Guid jobId,
        MentionJobStatus from,
        MentionJobStatus to,
        CancellationToken ct = default);

    Task SetFailedAsync(Guid jobId, string errorMessage, CancellationToken ct = default);

    Task SetCompletedAsync(Guid jobId, CancellationToken ct = default);
}
```

---

## `MentionDetector` (Application utility — pure logic)

```csharp
// MeisterProPR.Application/Services/MentionDetector.cs
public static class MentionDetector
{
    /// <summary>
    /// Returns true if <paramref name="content"/> contains a mention of the
    /// reviewer identified by <paramref name="reviewerGuid"/> or
    /// <paramref name="displayName"/>.
    /// Step 1: VSTS identity markup &lt;at id="{guid}"&gt;
    /// Step 2: plain-text @DisplayName fallback
    /// </summary>
    public static bool IsMentioned(string content, Guid reviewerGuid, string displayName)
    {
        if (content.Contains($"id=\"{reviewerGuid}\"", StringComparison.OrdinalIgnoreCase))
            return true;
        return content.Contains($"@{displayName}", StringComparison.OrdinalIgnoreCase);
    }
}
```

---

## `Channel<MentionReplyJob>` DI Registration (Api → Application)

The channel is registered once in `Program.cs` and injected into both workers:

```csharp
// Program.cs
builder.Services.AddSingleton(
    Channel.CreateBounded<MentionReplyJob>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
    }));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<Channel<MentionReplyJob>>().Reader);
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<Channel<MentionReplyJob>>().Writer);
```

---

## Worker Interaction Diagram

```
MentionScanWorker (PeriodicTimer: MENTION_CRAWL_INTERVAL_SECONDS)
  │
  ├── per client config:
  │     IActivePrFetcher.GetRecentlyUpdatedPullRequestsAsync(updatedAfter: lastScannedAt)
  │     for each PR:
  │       if PR.LastUpdatedAt <= lastCommentSeenAt → skip
  │       IAdoPullRequestFetcher.FetchExistingThreadsAsync(prId)
  │       MentionDetector.IsMentioned(comment.Content, reviewerGuid, displayName)
  │       if mention found and not already processed:
  │         IMentionReplyJobRepository.AddAsync(job)
  │         ChannelWriter<MentionReplyJob>.WriteAsync(job)
  │     IMentionScanRepository.UpsertProjectScanAsync(lastScannedAt = now)
  │     IMentionScanRepository.UpsertPrScanAsync(lastCommentSeenAt = latest thread timestamp)
  │
  ▼
Channel<MentionReplyJob> (bounded 1000, BoundedChannelFullMode.Wait)
  │
  ▼
MentionReplyWorker (ChannelReader loop)
  │
  ├── per job:
  │     IMentionReplyJobRepository.TryTransitionAsync(Pending → Processing)
  │     IPullRequestFetcher.FetchAsync(prId) → PullRequest (with diff + threads)
  │     IMentionAnswerService.AnswerAsync(pullRequest, job.MentionText)
  │     IAdoThreadReplier.ReplyAsync(threadId, answerText)
  │     IMentionReplyJobRepository.SetCompletedAsync(jobId)
  │     [on failure]:
  │     IMentionReplyJobRepository.SetFailedAsync(jobId, error)
```
