using System.Diagnostics;
using MeisterProPR.Application.DTOs;
using MeisterProPR.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace MeisterProPR.Infrastructure.AzureDevOps;

/// <summary>ADO-backed implementation of <see cref="IAssignedPullRequestFetcher" />.</summary>
public sealed class AdoAssignedPrFetcher(
    VssConnectionFactory connectionFactory,
    ILogger<AdoAssignedPrFetcher> logger) : IAssignedPullRequestFetcher
{
    private static readonly ActivitySource ActivitySource = new("MeisterProPR.Infrastructure");

    /// <summary>
    ///     Resolves a <see cref="GitHttpClient" /> for the given organization URL.
    ///     Exposed as a delegate so tests can inject a mock without requiring a real VssConnection.
    /// </summary>
    internal Func<string, CancellationToken, Task<GitHttpClient>>? GitClientResolver { get; set; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AssignedPullRequestRef>> GetAssignedOpenPullRequestsAsync(
        CrawlConfigurationDto config,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("AdoAssignedPrFetcher.GetAssignedOpenPullRequests");
        activity?.SetTag("ado.org", config.OrganizationUrl);
        activity?.SetTag("ado.project", config.ProjectId);

        var gitClient = await this.ResolveGitClientAsync(config.OrganizationUrl, cancellationToken);

        var criteria = new GitPullRequestSearchCriteria
        {
            ReviewerId = config.ReviewerId,
            Status = PullRequestStatus.Active,
        };

        // null repositoryId = search all repos in the project
        var prs = await gitClient.GetPullRequestsAsync(
            config.ProjectId,
            null!,
            criteria,
            top: 200,
            userState: null,
            cancellationToken: cancellationToken);

        activity?.SetTag("ado.prs_found", prs.Count);

        var results = new List<AssignedPullRequestRef>(prs.Count);
        foreach (var pr in prs)
        {
            try
            {
                var iterations = await gitClient.GetPullRequestIterationsAsync(
                    config.ProjectId,
                    pr.Repository.Id.ToString(),
                    pr.PullRequestId,
                    false,
                    null,
                    cancellationToken);

                var latestIteration = iterations.Count > 0 ? iterations.Max(i => i.Id ?? 1) : 1;

                results.Add(
                    new AssignedPullRequestRef(
                        config.OrganizationUrl,
                        config.ProjectId,
                        pr.Repository.Id.ToString(),
                        pr.PullRequestId,
                        latestIteration));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get iterations for PR #{PrId}", pr.PullRequestId);
            }
        }

        return results;
    }

    private async Task<GitHttpClient> ResolveGitClientAsync(string orgUrl, CancellationToken ct)
    {
        if (this.GitClientResolver is not null)
        {
            return await this.GitClientResolver(orgUrl, ct);
        }

        var connection = await connectionFactory.GetConnectionAsync(orgUrl, ct);
        return connection.GetClient<GitHttpClient>();
    }
}