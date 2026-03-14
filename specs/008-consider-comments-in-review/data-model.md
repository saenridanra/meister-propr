# Data Model: Feature 008 — Consider Existing PR Comments in Review

## New Domain Value Objects

### `PrCommentThread`

Represents a single comment thread on a pull request (from any author, any iteration).

```csharp
namespace MeisterProPR.Domain.ValueObjects;

public sealed record PrCommentThread(
    int ThreadId,
    string? FilePath,        // null = PR-level thread (no file anchor)
    int? LineNumber,         // null = file-level thread (no line anchor)
    IReadOnlyList<PrThreadComment> Comments);
```

### `PrThreadComment`

A single comment within a thread.

```csharp
public sealed record PrThreadComment(
    string AuthorName,   // display name from ADO
    string Content);     // raw comment text
```

Both are co-located in `src/MeisterProPR.Domain/ValueObjects/PrCommentThread.cs`.

## Updated `PullRequest`

Added optional `ExistingThreads` parameter at the end (backward-compatible default `null`):

```csharp
public sealed record PullRequest(
    string OrganizationUrl,
    string ProjectId,
    string RepositoryId,
    int PullRequestId,
    int IterationId,
    string Title,
    string? Description,
    string SourceBranch,
    string TargetBranch,
    IReadOnlyList<ChangedFile> ChangedFiles,
    PrStatus Status = PrStatus.Active,
    IReadOnlyList<PrCommentThread>? ExistingThreads = null);
```

## No Database Changes

Threads are fetched live from ADO on demand. No persistence of thread state in the local database.

## ADO → Domain Mapping

| ADO SDK field | Domain field |
|---|---|
| `thread.Id` | `PrCommentThread.ThreadId` |
| `thread.ThreadContext?.FilePath` | `PrCommentThread.FilePath` |
| `thread.ThreadContext?.RightFileStart?.Line` | `PrCommentThread.LineNumber` |
| `comment.Author?.DisplayName ?? "Unknown"` | `PrThreadComment.AuthorName` |
| `comment.Content ?? ""` | `PrThreadComment.Content` |

**Exclusion rules**: Skip threads where `thread.IsDeleted == true` or `thread.Comments` is empty. Skip individual comments where `comment.IsDeleted == true`.
