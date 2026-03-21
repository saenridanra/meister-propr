using MeisterProPR.Application.Interfaces;
using MeisterProPR.Domain.Entities;
using MeisterProPR.Domain.Enums;
using MeisterProPR.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace MeisterProPR.Application.Services;

/// <summary>
///     Orchestrates the end-to-end process of handling a review job:
/// </summary>
/// <param name="jobs">The job repository for managing review jobs.</param>
/// <param name="prFetcher">The pull request fetcher for retrieving PR details.</param>
/// <param name="aiCore">The AI review core for performing the review.</param>
/// <param name="commentPoster">The comment poster for posting review results to Azure DevOps.</param>
/// <param name="reviewerManager">Adds the AI identity as an optional reviewer on the PR.</param>
/// <param name="clientRegistry">Registry for looking up per-client configuration.</param>
/// <param name="logger">The logger for logging review orchestration events.</param>
public sealed partial class ReviewOrchestrationService(
    IJobRepository jobs,
    IPullRequestFetcher prFetcher,
    IAiReviewCore aiCore,
    IAdoCommentPoster commentPoster,
    IAdoReviewerManager reviewerManager,
    IClientRegistry clientRegistry,
    ILogger<ReviewOrchestrationService> logger)
{
    /// <summary>
    ///     Processes the given review job end-to-end.
    /// </summary>
    public async Task ProcessAsync(ReviewJob job, CancellationToken ct)
    {
        // Guard 1: client must be associated
        if (job.ClientId is null)
        {
            LogNoClientAssociated(logger, job.Id);
            jobs.SetFailed(job.Id, "No client associated with job");
            return;
        }

        // Guard 2: client must have a reviewer identity configured
        var reviewerId = await clientRegistry.GetReviewerIdAsync(job.ClientId.Value, ct);
        if (reviewerId is null)
        {
            LogReviewerIdentityMissing(logger, job.ClientId.Value, job.Id);
            jobs.SetFailed(job.Id, $"Reviewer identity not configured for client {job.ClientId}");
            return;
        }

        try
        {
            LogReviewStarted(logger, job.Id, job.PullRequestId);

            var pr = await prFetcher.FetchAsync(
                job.OrganizationUrl,
                job.ProjectId,
                job.RepositoryId,
                job.PullRequestId,
                job.IterationId,
                job.ClientId,
                ct);

            // EC-002: PR was closed or abandoned before the review could run.
            if (pr.Status != PrStatus.Active)
            {
                LogPrNoLongerActive(logger, job.PullRequestId, pr.Status, job.Id);
                jobs.SetFailed(job.Id, "PR was closed or abandoned before review could begin");
                return;
            }

            // Add AI identity as optional reviewer before posting any comments.
            await reviewerManager.AddOptionalReviewerAsync(
                job.OrganizationUrl,
                job.ProjectId,
                job.RepositoryId,
                job.PullRequestId,
                reviewerId.Value,
                job.ClientId,
                ct);

            var result = await aiCore.ReviewAsync(pr, ct);

            await commentPoster.PostAsync(
                job.OrganizationUrl,
                job.ProjectId,
                job.RepositoryId,
                job.PullRequestId,
                job.IterationId,
                result,
                job.ClientId,
                pr.ExistingThreads,
                ct);

            jobs.SetResult(job.Id, result);
            LogReviewCompleted(logger, job.Id);
        }
        catch (Exception ex)
        {
            LogReviewFailed(logger, job.Id, ex);
            jobs.SetFailed(job.Id, ex.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {JobId} has no client associated — failing")]
    private static partial void LogNoClientAssociated(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reviewer identity not configured for client {ClientId} — failing job {JobId}")]
    private static partial void LogReviewerIdentityMissing(ILogger logger, Guid clientId, Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting review for job {JobId} PR#{PrId}")]
    private static partial void LogReviewStarted(ILogger logger, Guid jobId, int prId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PR #{PrId} is no longer active (status: {Status}) — failing job {JobId}")]
    private static partial void LogPrNoLongerActive(ILogger logger, int prId, PrStatus status, Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Completed review for job {JobId}")]
    private static partial void LogReviewCompleted(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Review failed for job {JobId}")]
    private static partial void LogReviewFailed(ILogger logger, Guid jobId, Exception ex);
}
