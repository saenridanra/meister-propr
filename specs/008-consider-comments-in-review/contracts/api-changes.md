# API Contract Changes: Feature 008

## REST API

**No changes.** This feature is purely internal — no new endpoints, no modified request/response shapes.

`openapi.json` does not need to be updated.

## Internal Interface Changes

### `IAdoCommentPoster.PostAsync`

Added optional parameter (non-breaking):

```csharp
// Before
Task PostAsync(string organizationUrl, string projectId, string repositoryId,
    int pullRequestId, int iterationId, ReviewResult result,
    Guid? clientId = null, CancellationToken cancellationToken = default);

// After
Task PostAsync(string organizationUrl, string projectId, string repositoryId,
    int pullRequestId, int iterationId, ReviewResult result,
    Guid? clientId = null,
    IReadOnlyList<PrCommentThread>? existingThreads = null,
    CancellationToken cancellationToken = default);
```

Default `null` preserves backward compatibility — all existing call sites compile without changes.
