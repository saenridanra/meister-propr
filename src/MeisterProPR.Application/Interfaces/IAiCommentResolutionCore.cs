using MeisterProPR.Domain.ValueObjects;

namespace MeisterProPR.Application.Interfaces;

/// <summary>
///     AI core for evaluating whether a reviewer-authored pull-request comment thread has been resolved.
///     Provides two prompt paths: code-change evaluation and conversational reply.
/// </summary>
public interface IAiCommentResolutionCore
{
    /// <summary>
    ///     Evaluates whether a code change addresses the issue raised in <paramref name="thread" />.
    ///     Called when a new PR iteration (commit) has been detected since the thread was last processed.
    /// </summary>
    /// <param name="thread">The reviewer-authored comment thread to evaluate.</param>
    /// <param name="pr">The pull request containing the latest diff and full file contents.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    ///     A <see cref="ThreadResolutionResult" /> indicating whether the issue is resolved and
    ///     an optional reply to post in the thread.
    /// </returns>
    Task<ThreadResolutionResult> EvaluateCodeChangeAsync(
        PrCommentThread thread,
        PullRequest pr,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Generates a conversational response to new human replies in <paramref name="thread" />,
    ///     when no new commits have been pushed since the thread was last processed.
    /// </summary>
    /// <param name="thread">The reviewer-authored comment thread containing the new replies.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    ///     A <see cref="ThreadResolutionResult" /> with <c>IsResolved = false</c> and
    ///     a <c>ReplyText</c> to post as a conversational follow-up.
    /// </returns>
    Task<ThreadResolutionResult> EvaluateConversationalReplyAsync(
        PrCommentThread thread,
        CancellationToken cancellationToken = default);
}
