using MeisterProPR.Application.Interfaces;
using MeisterProPR.Domain.Enums;
using MeisterProPR.Domain.ValueObjects;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace MeisterProPR.Infrastructure.AzureDevOps;

public sealed class AdoCommentPoster(
    VssConnectionFactory connectionFactory,
    IClientAdoCredentialRepository credentialRepository) : IAdoCommentPoster
{
    public async Task PostAsync(
        string organizationUrl,
        string projectId,
        string repositoryId,
        int pullRequestId,
        int iterationId,
        ReviewResult result,
        Guid? clientId = null,
        IReadOnlyList<PrCommentThread>? existingThreads = null,
        CancellationToken cancellationToken = default)
    {
        var credentials = clientId.HasValue
            ? await credentialRepository.GetByClientIdAsync(clientId.Value, cancellationToken)
            : null;
        var connection = await connectionFactory.GetConnectionAsync(organizationUrl, credentials, cancellationToken);
        var gitClient = connection.GetClient<GitHttpClient>();

        // Build a map of normalised file path → changeTrackingId for inline comment anchoring.
        // changeTrackingId is required by ADO to resolve a file thread against the correct diff.
        var changes = await gitClient.GetPullRequestIterationChangesAsync(
            projectId,
            repositoryId,
            pullRequestId,
            iterationId,
            cancellationToken: cancellationToken);
        var changeTrackingIds = (changes.ChangeEntries ?? [])
            .Where(c => c.Item?.Path is not null)
            .ToDictionary(
                c => NormalizePath(c.Item!.Path!),
                c => c.ChangeTrackingId);

        // Post summary as PR-level thread, skipping if a bot summary already exists.
        if (!HasBotSummary(existingThreads))
        {
            await CreateThreadAsync(
                gitClient,
                projectId,
                repositoryId,
                pullRequestId,
                $"**AI Review Summary**\n\n{result.Summary}",
                null,
                null,
                cancellationToken);
        }

        // Post each inline comment, skipping locations the bot has already covered.
        foreach (var comment in result.Comments)
        {
            CommentThreadContext? threadContext = null;
            GitPullRequestCommentThreadContext? prThreadContext = null;
            string? normalizedFilePath = null;

            if (comment.FilePath is not null)
            {
                // ADO requires paths with a leading '/'; normalise in case the AI omits it.
                normalizedFilePath = NormalizePath(comment.FilePath);
                threadContext = new CommentThreadContext
                {
                    FilePath = normalizedFilePath,
                    RightFileStart = comment.LineNumber.HasValue
                        ? new CommentPosition { Line = comment.LineNumber.Value, Offset = 1 }
                        : null,
                    RightFileEnd = comment.LineNumber.HasValue
                        ? new CommentPosition { Line = comment.LineNumber.Value, Offset = 1 }
                        : null,
                };

                // pullRequestThreadContext anchors the thread to the correct iteration diff.
                if (changeTrackingIds.TryGetValue(normalizedFilePath, out var trackingId))
                {
                    prThreadContext = new GitPullRequestCommentThreadContext
                    {
                        ChangeTrackingId = trackingId,
                        IterationContext = new CommentIterationContext
                        {
                            FirstComparingIteration = (short)iterationId,
                            SecondComparingIteration = (short)iterationId,
                        },
                    };
                }
            }

            if (HasBotThreadAt(existingThreads, normalizedFilePath, comment.LineNumber))
            {
                continue;
            }

            var severityPrefix = comment.Severity switch
            {
                CommentSeverity.Error => "ERROR",
                CommentSeverity.Warning => "WARNING",
                CommentSeverity.Suggestion => "SUGGESTION",
                _ => "INFO",
            };

            await CreateThreadAsync(
                gitClient,
                projectId,
                repositoryId,
                pullRequestId,
                $"{severityPrefix}: {comment.Message}",
                threadContext,
                prThreadContext,
                cancellationToken);
        }
    }

    /// <summary>
    ///     Returns <c>true</c> if the given comment content was authored by the bot.
    ///     Identified by well-known text prefixes rather than by identity lookup.
    /// </summary>
    internal static bool IsBotContent(string content) =>
        content.StartsWith("**AI Review Summary**", StringComparison.Ordinal) ||
        content.StartsWith("ERROR: ", StringComparison.Ordinal) ||
        content.StartsWith("WARNING: ", StringComparison.Ordinal) ||
        content.StartsWith("SUGGESTION: ", StringComparison.Ordinal) ||
        content.StartsWith("INFO: ", StringComparison.Ordinal);

    /// <summary>
    ///     Returns <c>true</c> if a bot-authored PR-level summary thread already exists.
    /// </summary>
    internal static bool HasBotSummary(IReadOnlyList<PrCommentThread>? threads) =>
        (threads ?? []).Any(t =>
            t.FilePath is null &&
            t.Comments.Any(c => c.Content.StartsWith("**AI Review Summary**", StringComparison.Ordinal)));

    /// <summary>
    ///     Returns <c>true</c> if a bot-authored thread already exists at the given file path and line number.
    /// </summary>
    internal static bool HasBotThreadAt(
        IReadOnlyList<PrCommentThread>? threads,
        string? filePath,
        int? lineNumber) =>
        filePath is not null &&
        (threads ?? []).Any(t =>
            t.FilePath == filePath &&
            t.LineNumber == lineNumber &&
            t.Comments.Any(c => IsBotContent(c.Content)));

    private static string NormalizePath(string path) =>
        path.StartsWith('/') ? path : "/" + path;

    private static async Task CreateThreadAsync(
        GitHttpClient gitClient,
        string projectId,
        string repositoryId,
        int pullRequestId,
        string message,
        CommentThreadContext? threadContext,
        GitPullRequestCommentThreadContext? prThreadContext,
        CancellationToken ct)
    {
        var thread = new GitPullRequestCommentThread
        {
            Comments = [new Comment { Content = message, CommentType = CommentType.Text }],
            Status = CommentThreadStatus.Active,
            ThreadContext = threadContext,
            PullRequestThreadContext = prThreadContext,
        };
        await gitClient.CreateThreadAsync(
            thread,
            repositoryId,
            pullRequestId,
            projectId,
            ct);
    }

    private static string NormalizePath(string path)
    {
        return path.StartsWith('/') ? path : "/" + path;
    }
}