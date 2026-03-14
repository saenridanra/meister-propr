# Implementation Plan: Feature 008 — Consider Existing PR Comments in Review

**Branch**: `008-consider-comments-in-review` | **Date**: 2026-03-11 | **Spec**: spec.md

## Summary

Fetch existing PR comment threads from ADO before each review run. Pass them to the AI for contextual awareness and use them in the comment poster to skip duplicate bot-authored threads.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Microsoft.TeamFoundationServer.Client (existing), Microsoft.Extensions.AI (existing)
**Storage**: N/A — threads fetched live, not persisted
**Testing**: xUnit + NSubstitute
**Target Platform**: Linux container (ASP.NET Core)
**Performance Goals**: One additional `GetThreadsAsync` ADO call per review job (acceptable)
**Constraints**: No API contract changes, backward-compatible interface changes

## Constitution Check

- [x] **I. API-Contract-First** — No REST endpoint changes. `openapi.json` unchanged.
- [x] **II. Test-First** — `[TEST]` tasks defined first in `tasks.md`; all failing before implementation.
- [x] **III. Container-First** — No Windows APIs. All new code runs on Linux.
- [x] **IV. Clean Architecture** — `PrCommentThread` lives in Domain; Infrastructure fetches/maps it. Dependency arrows all point inward.
- [x] **V. Security** — No credentials or tokens flow through new code paths. ADO token not logged.
- [x] **VI. Job Reliability** — No new job types. Existing `IJobRepository` lifecycle unchanged.
- [x] **VII. Observability** — Log warning on thread fetch failure. Structured logging for skipped threads.

## Project Structure

```text
src/
├── MeisterProPR.Domain/ValueObjects/
│   └── PrCommentThread.cs          # NEW
├── MeisterProPR.Application/
│   ├── Interfaces/IAdoCommentPoster.cs   # MODIFY
│   └── Services/ReviewOrchestrationService.cs  # MODIFY
└── MeisterProPR.Infrastructure/
    ├── AzureDevOps/AdoPullRequestFetcher.cs  # MODIFY
    ├── AzureDevOps/AdoCommentPoster.cs       # MODIFY
    ├── AzureDevOps/NoOpAdoCommentPoster.cs   # MODIFY
    └── AI/ReviewPrompts.cs                   # MODIFY

tests/
├── MeisterProPR.Domain.Tests/ValueObjects/
│   └── PrCommentThreadTests.cs     # NEW
├── MeisterProPR.Application.Tests/Services/
│   └── ReviewOrchestrationServiceTests.cs  # MODIFY
└── MeisterProPR.Infrastructure.Tests/
    ├── AzureDevOps/AdoCommentPosterDeduplicationTests.cs  # NEW
    └── AI/ReviewPromptsExistingThreadsTests.cs            # NEW

specs/008-consider-comments-in-review/
├── spec.md, research.md, data-model.md, quickstart.md, plan.md
├── contracts/api-changes.md
└── tasks.md
```

## Key Implementation Notes

### Bot detection predicate
```csharp
internal static bool IsBotContent(string content) =>
    content.StartsWith("**AI Review Summary**", StringComparison.Ordinal) ||
    content.StartsWith("ERROR: ", StringComparison.Ordinal) ||
    content.StartsWith("WARNING: ", StringComparison.Ordinal) ||
    content.StartsWith("SUGGESTION: ", StringComparison.Ordinal) ||
    content.StartsWith("INFO: ", StringComparison.Ordinal);
```

Expose as `internal static` for testability without a real ADO client.

### Graceful degradation
If `GetThreadsAsync` throws, log warning at `LogLevel.Warning` and proceed with `existingThreads = []`. Never let thread-fetch failure block the review.
