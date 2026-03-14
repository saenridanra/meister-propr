# Tasks: Feature 008 — Consider Existing PR Comments in Review

> **Rule**: All `[TEST]` tasks must be confirmed FAILING before the paired implementation task runs.

## T001 [TEST] PrCommentThread value object tests
Create `tests/MeisterProPR.Domain.Tests/ValueObjects/PrCommentThreadTests.cs` with:
- `PrCommentThread_WithFilePath_ReturnsCorrectProperties`
- `PrCommentThread_WithoutFilePath_IsNullFilePath`
- `PrCommentThread_WithMultipleComments_PreservesOrder`
- `PrThreadComment_StoresAuthorAndContent`

**Status**: [ ] Failing confirmed

---

## T002 Create `PrCommentThread` and `PrThreadComment` domain value objects
Create `src/MeisterProPR.Domain/ValueObjects/PrCommentThread.cs`.

**Depends on**: T001 (tests exist and fail)
**Verification**: T001 tests pass.

---

## T003 [TEST] Bot content detection tests
Create `tests/MeisterProPR.Infrastructure.Tests/AzureDevOps/AdoCommentPosterDeduplicationTests.cs`:
- `IsBotContent_SummaryPrefix_ReturnsTrue`
- `IsBotContent_ErrorPrefix_ReturnsTrue`
- `IsBotContent_WarningPrefix_ReturnsTrue`
- `IsBotContent_SuggestionPrefix_ReturnsTrue`
- `IsBotContent_InfoPrefix_ReturnsTrue`
- `IsBotContent_UserComment_ReturnsFalse`
- `IsBotContent_EmptyString_ReturnsFalse`

**Status**: [ ] Failing confirmed

---

## T004 [TEST] Deduplication skip tests
Extend deduplication test file (or same file):
- `Deduplication_SummaryAlreadyExists_SummaryThreadNotReposted` — use `ReviewResult` + threads; verify the summary creation is skipped
- `Deduplication_InlineBotThread_SameFileLine_SkipsPost`
- `Deduplication_InlineBotThread_DifferentLine_Posts`
- `Deduplication_NoExistingThreads_PostsAll`
- `Deduplication_ExistingUserThread_PostsBotComment`

**Status**: [ ] Failing confirmed

---

## T005 [TEST] ReviewPrompts thread context tests
Create `tests/MeisterProPR.Infrastructure.Tests/AI/ReviewPromptsExistingThreadsTests.cs`:
- `BuildUserMessage_WithExistingThreads_IncludesExistingThreadsSection`
- `BuildUserMessage_WithNoExistingThreads_OmitsExistingThreadsSection`
- `BuildUserMessage_WithNullExistingThreads_OmitsExistingThreadsSection`
- `BuildUserMessage_ThreadWithFilePath_IncludesFileAndLine`
- `BuildUserMessage_PrLevelThread_ShowsPrLevel`

**Status**: [ ] Failing confirmed

---

## T006 [TEST] Orchestration service passes threads to poster
Add to `tests/MeisterProPR.Application.Tests/Services/ReviewOrchestrationServiceTests.cs`:
- `ProcessAsync_PassesExistingThreadsToCommentPoster`

**Status**: [ ] Failing confirmed

---

## T007 Update `PullRequest` domain record
Modify `src/MeisterProPR.Domain/ValueObjects/PullRequest.cs`: add `IReadOnlyList<PrCommentThread>? ExistingThreads = null` at end.

**Depends on**: T002
**Verification**: All existing PullRequest tests still pass; T001 tests pass.

---

## T008 Update `IAdoCommentPoster` interface
Modify `src/MeisterProPR.Application/Interfaces/IAdoCommentPoster.cs`: add optional `existingThreads` parameter before `CancellationToken`.

---

## T009 Update `ReviewOrchestrationService`
Modify `src/MeisterProPR.Application/Services/ReviewOrchestrationService.cs`: pass `pr.ExistingThreads` to `commentPoster.PostAsync`.

**Depends on**: T008
**Verification**: T006 test passes.

---

## T010 Update `AdoPullRequestFetcher` — fetch threads
Modify `src/MeisterProPR.Infrastructure/AzureDevOps/AdoPullRequestFetcher.cs`:
- Call `gitClient.GetThreadsAsync` after fetching changed files
- Map to `PrCommentThread` domain objects
- Handle failure with warning log + empty list
- Return updated `PullRequest` with `ExistingThreads`

**Depends on**: T007

---

## T011 Update `ReviewPrompts.BuildUserMessage` — thread context
Modify `src/MeisterProPR.Infrastructure/AI/ReviewPrompts.cs`: append existing threads section when `pr.ExistingThreads` has entries.

**Verification**: T005 tests pass.

---

## T012 Update `AdoCommentPoster` — deduplication
Modify `src/MeisterProPR.Infrastructure/AzureDevOps/AdoCommentPoster.cs`:
- Accept `existingThreads` parameter
- Extract `internal static bool IsBotContent(string content)` helper
- Skip summary post if bot summary already exists
- Skip inline post if bot thread already at same file/line

**Verification**: T003 + T004 tests pass.

---

## T013 Update `NoOpAdoCommentPoster` stub
Modify `src/MeisterProPR.Infrastructure/AzureDevOps/NoOpAdoCommentPoster.cs`: add `existingThreads` parameter to match updated interface.

---

## T014 Run full test suite
```bash
dotnet test
dotnet build
```
All tests pass. No warnings.
