namespace MeisterProPR.Domain.ValueObjects;

/// <summary>
///     Represents an existing comment thread on a pull request, from any author or iteration.
/// </summary>
/// <param name="ThreadId">ADO thread identifier.</param>
/// <param name="FilePath">File path the thread is anchored to, or <c>null</c> for PR-level threads.</param>
/// <param name="LineNumber">Line number the thread is anchored to, or <c>null</c> for file- or PR-level threads.</param>
/// <param name="Comments">Comments within this thread, ordered chronologically.</param>
public sealed record PrCommentThread(
    int ThreadId,
    string? FilePath,
    int? LineNumber,
    IReadOnlyList<PrThreadComment> Comments);

/// <summary>
///     Represents a single comment within a <see cref="PrCommentThread" />.
/// </summary>
/// <param name="AuthorName">Display name of the comment author from ADO.</param>
/// <param name="Content">Raw text content of the comment.</param>
public sealed record PrThreadComment(
    string AuthorName,
    string Content);
