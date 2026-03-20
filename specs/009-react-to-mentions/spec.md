# Feature Specification: React to Mentions in PR Comments

**Feature Branch**: `009-react-to-mentions`  
**Created**: 2026-03-20  
**Status**: Approved

## Clarifications

### Session 2026-03-20

- Q: Should crawl state (scan watermarks per PR) survive service restarts? → A: Yes — persist `MentionScanRecord` in the database so the service picks up where it left off after a reboot.
- Q: What background processing model should be used for processing mention replies? → A: Producer/consumer queue backed by the database. Queue items (`MentionReplyJob`) are persisted. On service startup, unprocessed queue entries are hydrated from the database into the worker queue.
- Q: How should the bot detect that it has been mentioned in a comment? → A: Use the client's reviewer identity GUID (obtained from `VssConnection.AuthorizedIdentity`) to match ADO mention metadata — not string matching on a display name.
- Q: Should there be one worker per client, or shared singleton workers? → A: Two shared singleton `BackgroundService` workers — `MentionScanWorker` (producer) and `MentionReplyWorker` (consumer) — iterating all clients each cycle, with `clientId` carried on each job record. Evaluated alternatives: (B) one worker instance per client — rejected because `IHostedService` has no native dynamic lifecycle support, wastes resources when clients are idle, and diverges from existing patterns; (C) one combined scan+reply worker per client — rejected because slow AI reply calls would block the scan cycle and the queue pattern has no natural home. Option A mirrors the existing `AdoPrCrawlerWorker` + `ReviewJobWorker` design and can be replaced with per-client workers if SLA isolation becomes a hard requirement.
- Q: What in-memory queue mechanism should `MentionReplyWorker` use, and how does this relate to the existing `ReviewJobWorker` DB-poll pattern? → A: Use a `Channel<T>`-based producer/consumer queue. This intentionally introduces a Channel-based pattern distinct from the DB-poll loop in `ReviewJobWorker`. The existing worker may be reworked to the same Channel pattern in a future iteration.
- Q: Does the existing comment-posting infrastructure support replying within an existing thread? → A: No — the current infrastructure only creates new top-level threads. Posting a reply into an existing thread (by thread ID) is a new capability that must be designed and built as part of this feature. The implementation plan must account for this.
- Q: How should mention detection handle comments without parseable VSTS identity markup? → A: Option A — GUID-first with display-name fallback: parse VSTS `<at id="{guid}">` markup in comment content first using `VssConnection.AuthorizedIdentity.Id`; if no markup match is found, fall back to plain-text substring match on `client.DisplayName`.
- Q: Which identity value is authoritative for the reviewer GUID used in mention detection? → A: `VssConnection.AuthorizedIdentity.Id` from the live client connection. This is the same value stored in `clients.reviewer_id`; the live connection value is authoritative and used at detection time.
- Q: How should `MentionScanWorker` discover the set of active PRs to check per crawl configuration, including PRs where the bot is not a reviewer? → A: Option D — call `GetPullRequestsByProjectAsync` with `minLastUpdateDate` = the last scan watermark for that project (per `CrawlConfiguration`). This returns only PRs with recent activity, naturally bounding the scan without a separate discovery table, and covers all PRs regardless of reviewer assignment. Evaluated and rejected: (A) full unrestricted project query every cycle — too many results on large projects even with no changes; (B) reviewer-assigned set only — contradicts FR-002; (C) separate known-PR table with periodic sweep — adds a third moving part unnecessarily.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Developer Asks a Question in a PR Comment (Priority: P1)

A developer is reviewing a pull request and wants quick AI-powered insight. They mention the bot by name in a PR comment (e.g., `@MeisterProPR what is the risk of this change?`). The bot detects the mention, uses the PR's code changes and description as context, and replies directly in the same comment thread with a relevant answer.

**Why this priority**: This is the core value proposition of the feature — turning the bot into an interactive, on-demand assistant inside the PR workflow. It is the minimal viable behaviour.

**Independent Test**: Post a comment mentioning the bot on any active PR (including one the bot is not a reviewer on); within the next scan cycle, verify the bot posts a reply in that same thread that addresses the question.

**Acceptance Scenarios**:

1. **Given** a developer posts `@MeisterProPR explain the performance impact of this change` on a PR the bot is NOT a reviewer on, **When** the next scan cycle runs, **Then** the bot replies in that same thread with an AI-generated answer informed by the PR's diff and description.
2. **Given** a developer posts `@MeisterProPR summarise the changes in plain English` on a PR the bot IS already a reviewer on, **When** the scan runs, **Then** the bot replies in the same thread without creating a new full review job.
3. **Given** the bot has already replied to a specific mention in a previous cycle, **When** the next scan cycle runs, **Then** no duplicate reply is posted.

---

### User Story 2 — Bot Gathers PR Context Before Answering (Priority: P2)

When the bot encounters a mention it has not yet answered, it enriches its response using contextual information available in the pull request: the PR title, description, the code diff of affected files, and any existing comment threads. This ensures the answer is specific and useful rather than generic.

**Why this priority**: Without additional context the bot's replies are shallow. Context enrichment is what differentiates a useful answer from a generic one — but it requires that Story 1 already functions and need not block it.

**Independent Test**: Post a mention on a PR that has a non-trivial diff and description; verify the bot's reply references details from the diff or description, demonstrating it consumed that context.

**Acceptance Scenarios**:

1. **Given** a developer mentions the bot asking "is this change safe?", **When** the bot replies, **Then** the reply references specific code or description content from the PR (not a generic non-contextual answer).
2. **Given** the PR has existing discussion threads relevant to the question, **When** the bot generates its reply, **Then** the reply may acknowledge or refer to points already made in those threads.
3. **Given** the PR diff or description is unavailable due to a transient error, **When** the bot processes the mention, **Then** the bot posts a reply acknowledging limited context rather than failing silently or crashing.

---

### User Story 3 — Efficient Scanning Without Excessive ADO Requests (Priority: P3)

The backend scans for new unanswered mentions frequently enough to provide timely responses but without making excessive or redundant API calls. PRs that have no new comments since the last scan are skipped. The scan also covers PRs where the bot has not been added as a reviewer.

**Why this priority**: Efficiency is critical for production viability. Over-polling risks ADO rate-limiting and degrades overall system performance, but this concern only matters once Stories 1 and 2 are working.

**Independent Test**: Monitor ADO API call counts over multiple scan cycles for a set of PRs with no new activity; verify call count does not grow linearly with the number of idle PRs.

**Acceptance Scenarios**:

1. **Given** 50 active PRs and no new comments since the last scan, **When** the scan cycle runs, **Then** the call volume to ADO is significantly less than fetching full thread lists for all 50 PRs.
2. **Given** a PR has a new comment mentioning the bot, **When** the scan cycle runs, **Then** it is detected and processed without requiring a full re-fetch of unrelated PRs.
3. **Given** the bot is not currently a reviewer on a PR, **When** a user mentions the bot in a comment on that PR, **Then** the mention is still detected and answered within the scan interval.

---

### Edge Cases

- What if the same comment mentions the bot twice? → Only one reply per mention comment; the second `@` in the same comment text is ignored.
- What if the bot is mentioned in a reply within a thread it already answered in a previous cycle? → Each new unanswered mention comment (a new reply in a thread) triggers a new reply, as long as the bot has not already replied to that specific comment.
- What if the PR is abandoned or completed before the scan runs? → Mentions in closed/abandoned PRs are silently skipped; no reply is posted.
- What if the ADO thread-fetch call fails for a specific PR? → That PR is skipped for this cycle and retried on the next scan cycle.
- What if the AI fails to generate a reply? → The mention remains marked as unprocessed; it is retried on the next cycle (no partial or empty reply is posted).
- What if the mention contains no actionable content or question? → The bot posts a polite acknowledgement reply indicating it needs a clearer question.
- What if multiple people mention the bot in the same PR within the same scan window? → Each distinct unanswered mention comment receives its own reply.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST continuously scan for PR comment threads containing unanswered mentions of the bot identity across all active PRs for each client configuration.
- **FR-002**: The scan MUST discover active PRs per crawl configuration by calling the ADO project-scoped PR list API with a `minLastUpdateDate` filter set to the last scan watermark for that configuration. This returns only recently-updated PRs regardless of reviewer assignment, covering PRs where the bot has NOT been added as a reviewer. (Evaluated and rejected: unrestricted full-project query per cycle — too many results on large projects even with no changes; reviewer-assigned set only — contradicts this requirement; separate known-PR discovery table — unnecessary complexity.)
- **FR-003**: For each unanswered mention, the system MUST fetch the PR's title, description, code diff of changed files, and existing comment threads as context before generating a reply.
- **FR-004**: The system MUST post the AI-generated reply as a direct reply within the same comment thread where the mention occurred — not as a new top-level thread.
- **FR-005**: The system MUST NOT post duplicate replies — if it has already replied to a specific mention comment, it MUST skip it on subsequent scan cycles.
- **FR-006**: The scan MUST be efficient at two levels: (1) PR discovery uses the `minLastUpdateDate` watermark per crawl configuration (FR-002) to avoid returning unchanged PRs; (2) per-PR thread fetching is skipped for PRs where the latest comment timestamp has not advanced beyond the per-PR watermark in `MentionScanRecord`.
- **FR-007**: The AI-generated reply MUST be based on the user's question intent and the PR context; it MUST NOT initiate a full code review — only answer the specific question asked.
- **FR-008**: If the PR diff, description, or thread context is unavailable due to a transient error, the system MUST still post a reply acknowledging limited context rather than silently failing.
- **FR-009**: Mentions in closed, abandoned, or completed PRs MUST be silently skipped with no reply posted.
- **FR-010**: The mention-scanning cycle interval MUST be configurable independently of the existing PR review crawl interval, with a minimum enforcement of 10 seconds.
- **FR-011**: Background processing MUST be implemented as two shared singleton `BackgroundService` workers: `MentionScanWorker` (producer) iterates all clients and their crawl-config-scoped PRs each cycle, enqueuing `MentionReplyJob` items for each unanswered mention; `MentionReplyWorker` (consumer) drains the queue via a `Channel<T>`-based producer/consumer queue independently, resolving ADO credentials via the `clientId` on each job. Queue items MUST be persisted in the database. On service startup, all unprocessed `MentionReplyJob` entries MUST be hydrated from the database into the Channel so no work is lost across reboots. (Evaluated and rejected: one worker instance per client due to dynamic lifecycle complexity and resource overhead; one combined scan+reply worker per client due to tight coupling between discovery and AI reply latency. Note: the existing `ReviewJobWorker` uses a DB-poll pattern instead of a Channel; that worker may be reworked to this same Channel pattern in a future iteration.)
- **FR-012**: Mention detection MUST use a two-step approach: (1) parse VSTS identity markup (`<at id="{guid}">`) in the comment's raw content using `VssConnection.AuthorizedIdentity.Id` (the live reviewer identity GUID for the client's connection, consistent with the value stored in `clients.reviewer_id`) as the target GUID; (2) if no markup match is found, fall back to a plain-text substring match on the client's display name. A comment is considered a mention if either step matches.
- **FR-013**: The system MUST support posting a reply comment directly into an existing ADO pull request thread (identified by thread ID). This is a new infrastructure capability — the current comment-posting implementation only creates new top-level threads. The implementation plan MUST account for this gap.

### Key Entities

- **MentionScanRecord**: Tracks scan state at two levels. (1) Per crawl configuration: the `LastProjectScanAt` timestamp used as `minLastUpdateDate` when querying ADO for recently-updated PRs. (2) Per PR: the `LastCommentSeenAt` watermark used to skip thread re-fetches for PRs with no new comments. **Both are persisted in the database** and survive service reboots.
- **MentionReplyJob**: Represents a pending or completed task to reply to a specific unanswered mention, including the mention comment's ID, its thread ID, the PR reference, and the client it belongs to. **Persisted in the database** as a durable queue; unprocessed jobs are hydrated back into the worker queue on service startup.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer's `@mention` question receives a bot reply within two scan cycle intervals under normal operating conditions.
- **SC-002**: Zero duplicate bot replies are posted across any number of consecutive scan cycles for the same mention comment.
- **SC-003**: For PRs with no new comments since the last scan cycle, the number of ADO API calls does not exceed one lightweight check per PR (e.g., watermark comparison), regardless of total PR count.
- **SC-004**: Bot replies reference at least one piece of information from the PR's diff, description, or existing threads, demonstrating context was consumed.
- **SC-005**: The feature does not degrade existing PR review job throughput — review job processing latency remains within 5% of pre-feature baseline when mention scanning is active.
- **SC-006**: `dotnet test` passes with coverage on all new mention scanning and reply generation logic.

## Assumptions

- The authoritative reviewer identity GUID for mention detection is `VssConnection.AuthorizedIdentity.Id` obtained from the live client connection at runtime. This value is consistent with `clients.reviewer_id` stored in the database. Mention detection uses VSTS identity markup parsing as the primary method and client display-name substring matching as a fallback.
- ADO's comment threads expose a timestamp or ID that is monotonically increasing, enabling watermark-based incremental scanning. If not natively available, comment IDs serve as a proxy.
- The scope of "all active PRs" is bounded by each client's configured ADO organisation and project(s) — not a global scan across all ADO tenants.
- Replies are strictly answer-only: the bot never votes, approves, requests changes, or modifies reviewer assignments when replying to a mention.
- The feature is additive and does not replace the existing full-review crawl flow; both can run simultaneously for the same PRs without interference.
