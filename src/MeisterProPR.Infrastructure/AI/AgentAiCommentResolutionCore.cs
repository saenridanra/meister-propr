using System.Text;
using System.Text.Json;
using MeisterProPR.Application.Interfaces;
using MeisterProPR.Domain.ValueObjects;
using Microsoft.Extensions.AI;

namespace MeisterProPR.Infrastructure.AI;

/// <summary>
///     AI implementation of <see cref="IAiCommentResolutionCore" /> that evaluates whether a
///     reviewer-authored comment thread has been resolved, using two distinct prompt paths:
///     (1) code-change evaluation and (2) conversational reply generation.
/// </summary>
public sealed class AgentAiCommentResolutionCore(IChatClient chatClient) : IAiCommentResolutionCore
{
    private const string CodeChangeSystemPrompt = """
                                                  You are an expert code reviewer. A pull request has received new commits since you last
                                                  commented on a thread. Evaluate whether the latest code changes have addressed your original
                                                  concern. Be conservative: only mark as resolved if you are confident the issue is fixed.
                                                  If in doubt, return resolved=false.

                                                  Respond with valid JSON ONLY — no markdown fences, no preamble.
                                                  Schema: { "resolved": true|false, "replyText": "<optional short reply or null>" }
                                                  """;

    private const string ConversationalSystemPrompt = """
                                                      You are an expert code reviewer participating in a code review discussion. A developer has
                                                      replied to one of your comments. Read the thread history carefully and decide:

                                                      1. If the developer has acknowledged the issue, confirmed they won't address it, explained why
                                                         it's acceptable as-is, or explicitly asked you to close/resolve the thread — mark resolved=true
                                                         and write a brief closing acknowledgement as replyText.
                                                      2. If the developer is asking a question, requesting clarification, or the issue is still open
                                                         — mark resolved=false and write a helpful response as replyText.

                                                      Be willing to close threads when the developer makes a reasonable case. Do not insist on code
                                                      changes if the developer explains why the current approach is acceptable.

                                                      Respond with valid JSON ONLY — no markdown fences, no preamble.
                                                      Schema: { "resolved": true|false, "replyText": "<your response to the developer>" }
                                                      """;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <inheritdoc />
    public async Task<ThreadResolutionResult> EvaluateCodeChangeAsync(
        PrCommentThread thread,
        PullRequest pr,
        CancellationToken cancellationToken = default)
    {
        var userMessage = BuildCodeChangeUserMessage(thread, pr);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, CodeChangeSystemPrompt),
            new(ChatRole.User, userMessage),
        };

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return ParseResult(response.Text ?? "");
    }

    /// <inheritdoc />
    public async Task<ThreadResolutionResult> EvaluateConversationalReplyAsync(
        PrCommentThread thread,
        CancellationToken cancellationToken = default)
    {
        var userMessage = BuildConversationalUserMessage(thread);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, ConversationalSystemPrompt),
            new(ChatRole.User, userMessage),
        };

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return ParseResult(response.Text ?? "");
    }

    private static string BuildCodeChangeUserMessage(PrCommentThread thread, PullRequest pr)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Pull Request: {pr.Title}");
        sb.AppendLine($"{pr.SourceBranch} → {pr.TargetBranch}");
        sb.AppendLine();
        sb.AppendLine("## Thread to Evaluate");
        AppendThread(sb, thread);

        if (pr.ChangedFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Changed Files (latest iteration)");
            foreach (var file in pr.ChangedFiles)
            {
                sb.AppendLine();
                sb.AppendLine($"=== {file.Path} [{file.ChangeType}] ===");
                sb.AppendLine("--- DIFF ---");
                sb.AppendLine(file.UnifiedDiff);
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("No file changes in this iteration.");
        }

        return sb.ToString();
    }

    private static string BuildConversationalUserMessage(PrCommentThread thread)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Thread History");
        AppendThread(sb, thread);
        return sb.ToString();
    }

    private static void AppendThread(StringBuilder sb, PrCommentThread thread)
    {
        var location = thread.FilePath is not null
            ? $"{thread.FilePath}{(thread.LineNumber.HasValue ? $":L{thread.LineNumber}" : "")}"
            : "(PR-level)";

        sb.AppendLine($"Thread at {location}:");
        foreach (var comment in thread.Comments)
        {
            sb.AppendLine($"  [{comment.AuthorName}]: {comment.Content}");
        }
    }

    private static ThreadResolutionResult ParseResult(string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<ResolutionDto>(json, JsonOptions);
            if (dto is null)
            {
                return new ThreadResolutionResult(false, null);
            }

            return new ThreadResolutionResult(dto.Resolved, dto.ReplyText);
        }
        catch (JsonException)
        {
            return new ThreadResolutionResult(false, null);
        }
    }

    private sealed record ResolutionDto(bool Resolved, string? ReplyText);
}
