using MeisterProPR.Domain.Entities;
using MeisterProPR.Domain.Enums;
using MeisterProPR.Domain.ValueObjects;
using MeisterProPR.Infrastructure.Data;
using MeisterProPR.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace MeisterProPR.Infrastructure.Tests.Repositories;

/// <summary>
///     Integration tests for <see cref="PostgresJobRepository" /> against a real PostgreSQL instance.
/// </summary>
[Collection("PostgresIntegration")]
public sealed class PostgresJobRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private MeisterProPRDbContext _dbContext = null!;
    private PostgresJobRepository _repo = null!;

    public async Task DisposeAsync()
    {
        await this._dbContext.DisposeAsync();
        await this._postgres.DisposeAsync();
    }

    [Fact]
    public async Task GetAllJobsAsync_Pagination_Works()
    {
        for (var i = 1; i <= 5; i++)
        {
            this._repo.Add(MakeJob(prId: i));
        }

        var (total, page1) = await this._repo.GetAllJobsAsync(2, 0, null);
        Assert.Equal(5, total);
        Assert.Equal(2, page1.Count);

        var (_, page2) = await this._repo.GetAllJobsAsync(2, 2, null);
        Assert.Equal(2, page2.Count);

        var (_, page3) = await this._repo.GetAllJobsAsync(2, 4, null);
        Assert.Single(page3);
    }

    // ── GetAllJobsAsync (T047) ────────────────────────────────────────────────

    [Fact]
    public async Task GetAllJobsAsync_ReturnsAllJobsNewestFirst()
    {
        var job1 = MakeJob(prId: 100);
        this._repo.Add(job1);
        // Brief delay to ensure job2 gets a strictly later SubmittedAt timestamp.
        await Task.Delay(10);
        var job2 = MakeJob(prId: 101);
        this._repo.Add(job2);

        var (total, items) = await this._repo.GetAllJobsAsync(100, 0, null);
        Assert.Equal(2, total);
        // newest first — job2 was added last
        Assert.Equal(job2.Id, items[0].Id);
    }

    [Fact]
    public async Task GetAllJobsAsync_StatusFilter_ReturnsOnlyMatchingJobs()
    {
        var pending = MakeJob(prId: 200);
        var processing = MakeJob(prId: 201);
        this._repo.Add(pending);
        this._repo.Add(processing);
        this._repo.TryTransition(processing.Id, JobStatus.Pending, JobStatus.Processing);

        var (total, items) = await this._repo.GetAllJobsAsync(100, 0, JobStatus.Processing);
        Assert.Equal(1, total);
        Assert.Equal(processing.Id, items[0].Id);
    }

    [Fact]
    public async Task GetProcessingJobsAsync_EmptyWhenNoneProcessing()
    {
        this._repo.Add(MakeJob(prId: 400));
        var result = await this._repo.GetProcessingJobsAsync();
        Assert.Empty(result);
    }

    // ── GetProcessingJobsAsync (T047) ─────────────────────────────────────────

    [Fact]
    public async Task GetProcessingJobsAsync_ReturnsOnlyProcessingJobs()
    {
        var j1 = MakeJob(prId: 300);
        var j2 = MakeJob(prId: 301);
        this._repo.Add(j1);
        this._repo.Add(j2);
        this._repo.TryTransition(j1.Id, JobStatus.Pending, JobStatus.Processing);

        var result = await this._repo.GetProcessingJobsAsync();
        Assert.Single(result);
        Assert.Equal(j1.Id, result[0].Id);
    }

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        var options = new DbContextOptionsBuilder<MeisterProPRDbContext>()
            .UseNpgsql(this._postgres.GetConnectionString())
            .Options;
        this._dbContext = new MeisterProPRDbContext(options);
        await this._dbContext.Database.MigrateAsync();
        this._repo = new PostgresJobRepository(this._dbContext);
    }

    // ── Add / GetById ─────────────────────────────────────────────────────────

    [Fact]
    public void Add_ThenGetById_ReturnsJob()
    {
        var job = MakeJob();
        this._repo.Add(job);

        var fetched = this._repo.GetById(job.Id);
        Assert.NotNull(fetched);
        Assert.Equal(job.Id, fetched.Id);
        Assert.Equal(JobStatus.Pending, fetched.Status);
    }

    [Fact]
    public void FindActiveJob_ReturnsJobForCompleted()
    {
        var job = MakeJob();
        this._repo.Add(job);
        this._repo.TryTransition(job.Id, JobStatus.Pending, JobStatus.Processing);
        this._repo.SetResult(job.Id, new ReviewResult("summary", []));

        var found = this._repo.FindActiveJob(
            job.OrganizationUrl,
            job.ProjectId,
            job.RepositoryId,
            job.PullRequestId,
            job.IterationId);
        Assert.NotNull(found);
    }

    [Fact]
    public void FindActiveJob_ReturnsNullForFailedJob()
    {
        // T039 / T009: Failed job should return null to allow retry
        var job = MakeJob();
        this._repo.Add(job);
        this._repo.TryTransition(job.Id, JobStatus.Pending, JobStatus.Processing);
        this._repo.SetFailed(job.Id, "test error");

        var found = this._repo.FindActiveJob(
            job.OrganizationUrl,
            job.ProjectId,
            job.RepositoryId,
            job.PullRequestId,
            job.IterationId);
        Assert.Null(found);
    }

    // ── FindActiveJob ─────────────────────────────────────────────────────────

    [Fact]
    public void FindActiveJob_ReturnsPendingJob()
    {
        var job = MakeJob();
        this._repo.Add(job);

        var found = this._repo.FindActiveJob(
            job.OrganizationUrl,
            job.ProjectId,
            job.RepositoryId,
            job.PullRequestId,
            job.IterationId);
        Assert.NotNull(found);
        Assert.Equal(job.Id, found.Id);
    }

    // ── GetAllForClient ───────────────────────────────────────────────────────

    [Fact]
    public void GetAllForClient_ReturnsOnlyMatchingClientJobs()
    {
        this._repo.Add(MakeJob("client-a", prId: 1));
        this._repo.Add(MakeJob("client-b", prId: 2));
        this._repo.Add(MakeJob("client-a", prId: 3));

        var result = this._repo.GetAllForClient("client-a");
        Assert.Equal(2, result.Count);
        Assert.All(result, j => Assert.Equal("client-a", j.ClientKey));
    }

    [Fact]
    public void GetById_UnknownId_ReturnsNull()
    {
        var result = this._repo.GetById(Guid.NewGuid());
        Assert.Null(result);
    }

    // ── GetPendingJobs ────────────────────────────────────────────────────────

    [Fact]
    public void GetPendingJobs_OrderedOldestFirst()
    {
        var job1 = MakeJob(prId: 10);
        var job2 = MakeJob(prId: 20);
        this._repo.Add(job1);
        this._repo.Add(job2);

        var pending = this._repo.GetPendingJobs();
        Assert.Equal(2, pending.Count);
        // oldest first — job1 was added first so SubmittedAt is earlier
        Assert.Equal(job1.Id, pending[0].Id);
    }

    // ── SetFailed ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetFailed_TransitionsToFailed()
    {
        var job = MakeJob();
        this._repo.Add(job);

        this._repo.SetFailed(job.Id, "ADO API error");

        var fetched = this._repo.GetById(job.Id);
        Assert.Equal(JobStatus.Failed, fetched!.Status);
        Assert.Equal("ADO API error", fetched.ErrorMessage);
        Assert.NotNull(fetched.CompletedAt);
    }

    // ── SetResult ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetResult_TransitionsToCompleted()
    {
        var job = MakeJob();
        this._repo.Add(job);

        var result = new ReviewResult("Looks good", [new ReviewComment(null, null, CommentSeverity.Info, "No issues")]);
        this._repo.SetResult(job.Id, result);

        var fetched = this._repo.GetById(job.Id);
        Assert.Equal(JobStatus.Completed, fetched!.Status);
        Assert.NotNull(fetched.Result);
        Assert.Equal("Looks good", fetched.Result.Summary);
        Assert.NotNull(fetched.CompletedAt);
    }

    // ── TryTransition ─────────────────────────────────────────────────────────

    [Fact]
    public void TryTransition_PendingToProcessing_ReturnsTrue()
    {
        var job = MakeJob();
        this._repo.Add(job);

        var result = this._repo.TryTransition(job.Id, JobStatus.Pending, JobStatus.Processing);
        Assert.True(result);

        var fetched = this._repo.GetById(job.Id);
        Assert.Equal(JobStatus.Processing, fetched!.Status);
        Assert.NotNull(fetched.ProcessingStartedAt);
    }

    [Fact]
    public void TryTransition_WrongFromStatus_ReturnsFalse()
    {
        var job = MakeJob();
        this._repo.Add(job);

        // job is Pending; trying to transition from Processing should fail
        var result = this._repo.TryTransition(job.Id, JobStatus.Processing, JobStatus.Completed);
        Assert.False(result);

        var fetched = this._repo.GetById(job.Id);
        Assert.Equal(JobStatus.Pending, fetched!.Status);
    }

    private static ReviewJob MakeJob(
        string? clientKey = "client-key-abc",
        string orgUrl = "https://dev.azure.com/org",
        string projectId = "proj",
        string repoId = "repo",
        int prId = 1,
        int iterationId = 1)
    {
        return new ReviewJob(Guid.NewGuid(), clientKey, orgUrl, projectId, repoId, prId, iterationId);
    }
}