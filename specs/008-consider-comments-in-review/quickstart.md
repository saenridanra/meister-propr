# Quickstart: Feature 008 — Consider Existing PR Comments in Review

## Testing the Feature Manually

### Prerequisites
- ADO connection configured (not stub mode)
- A real PR with at least one open review job that has already been reviewed once

### Steps

1. Run the server normally with a real ADO connection:
   ```bash
   dotnet run --project src/MeisterProPR.Api
   ```

2. Trigger a first review (via the crawler or a manual POST to `/reviews`). Verify the PR gets bot comments.

3. Trigger a second review of the **same iteration** (manually add a job via the API or restart the crawler). Verify:
   - No new summary thread is added (check PR thread count)
   - No duplicate inline comments appear at the same file/line locations

4. Add a developer reply to one of the bot's inline threads in ADO. Then trigger a new review of a **new iteration** (push a commit). Verify:
   - The AI prompt includes the prior thread with the developer reply
   - New bot comments are posted for the new iteration

### Stub Mode

When `ADO_STUB_PR=true`, `ExistingThreads` will be `null` (stub returns no threads). Deduplication is effectively disabled in stub mode — this is expected behaviour.

### Verifying AI Context

Enable debug logging and look for the "Existing Review Threads" section in the logged AI prompt (only visible if you add prompt logging to `AgentAiReviewCore`).
