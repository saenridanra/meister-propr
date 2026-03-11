# Feature Specification: Consider Existing PR Comments in Review

**Feature Branch**: `008-consider-comments-in-review`
**Created**: 2026-03-11
**Status**: Approved

## User Scenarios & Testing

### User Story 1 — No Duplicate Bot Comments on Retry (Priority: P1)

When a review job is retried (e.g., after a transient failure), the bot must not post duplicate comment threads on the same PR locations it has already commented on.

**Why this priority**: Duplicate comments degrade developer experience, pollute PRs, and erode trust in the tool.

**Independent Test**: Submit a review job twice for the same PR iteration; verify the PR has exactly one bot summary thread and one bot thread per code location.

**Acceptance Scenarios**:

1. **Given** a PR with an existing "AI Review Summary" bot thread, **When** a second review job runs for the same iteration, **Then** no new summary thread is posted.
2. **Given** a PR with an existing bot inline comment at `/src/Foo.cs:L42`, **When** the review produces the same finding, **Then** no duplicate thread is posted at that location.
3. **Given** a PR with no bot threads, **When** a review job runs, **Then** all findings are posted normally.

---

### User Story 2 — AI Has Context of Existing Comments (Priority: P2)

When generating a review, the AI must receive all existing comment threads (from any author) as context, so it can avoid re-flagging resolved issues and acknowledge developer responses.

**Why this priority**: Context-aware reviews are higher quality and reduce noise from already-addressed feedback.

**Independent Test**: Post a manual comment on a PR addressing a previous bot finding; run a new review job; verify the AI summary acknowledges the thread.

**Acceptance Scenarios**:

1. **Given** a PR has existing threads (bot + developer replies), **When** the AI review runs, **Then** the prompt includes those threads with author and content.
2. **Given** a PR has no existing threads, **When** the AI review runs, **Then** no thread context section appears in the prompt.
3. **Given** existing threads include a developer saying "Fixed in this iteration", **When** the AI reviews, **Then** the AI may acknowledge the fix in its summary.

---

### Edge Cases

- What if ADO `GetThreadsAsync` fails? → Log warning, proceed with review without thread context (graceful degradation).
- What if a bot thread has been manually deleted in ADO? → `IsDeleted=true` threads are excluded from deduplication checks — the bot may re-post.
- What if line numbers shift due to new commits? → Deduplication is best-effort by file path + line number; false negatives (re-post) are acceptable over false positives (suppress valid findings).
- What if `ExistingThreads` is null? → Treat as empty — post all comments.

## Requirements

### Functional Requirements

- **FR-001**: Before posting review results, the system MUST fetch all non-deleted comment threads from ADO for the pull request.
- **FR-002**: The AI prompt MUST include existing thread content (author, location, message) when threads exist.
- **FR-003**: The comment poster MUST skip posting a PR-level summary if a bot-authored summary thread already exists.
- **FR-004**: The comment poster MUST skip posting an inline comment if a bot-authored thread already exists at the same file path and line number.
- **FR-005**: Bot thread detection MUST use text-prefix matching on the first comment (no service account identity lookup required).
- **FR-006**: Thread fetching failure MUST be handled gracefully — the review continues without thread context.

### Key Entities

- **PrCommentThread**: Represents an existing PR comment thread with location (file path, line number) and a list of comments.
- **PrThreadComment**: A single comment within a thread, with author display name and text content.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Running a review job twice on the same PR iteration results in identical ADO state (no new threads on second run).
- **SC-002**: AI prompt for a PR with 3 existing threads contains all 3 threads' content.
- **SC-003**: `dotnet test` passes with ≥95% coverage on the new deduplication logic.
- **SC-004**: No changes to the public REST API contract (`openapi.json` unchanged).
