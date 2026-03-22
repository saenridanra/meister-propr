namespace MeisterProPR.Domain.Entities;

/// <summary>
///     Tracks the last-seen reply count for a single reviewer-authored comment thread on a pull request.
///     If the current reply count for a thread exceeds <see cref="LastSeenReplyCount" />, human replies
///     have been added and the system must generate a conversational response (FR-005 exception path).
///     Composite primary key: (<see cref="ReviewPrScanId" />, <see cref="ThreadId" />).
/// </summary>
public sealed class ReviewPrScanThread
{
    /// <summary>FK to the owning <see cref="ReviewPrScan" />.</summary>
    public Guid ReviewPrScanId { get; set; }

    /// <summary>ADO thread identifier within the pull request.</summary>
    public int ThreadId { get; set; }

    /// <summary>
    ///     The number of comments observed in this thread when it was last processed.
    ///     Compared against the current comment count to detect new replies.
    /// </summary>
    public int LastSeenReplyCount { get; set; }

    /// <summary>Navigation property back to the owning scan record.</summary>
    public ReviewPrScan ReviewPrScan { get; set; } = null!;
}
