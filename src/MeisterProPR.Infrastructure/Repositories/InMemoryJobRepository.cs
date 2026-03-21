using System.Collections.Concurrent;
using MeisterProPR.Application.Interfaces;
using MeisterProPR.Domain.Entities;
using MeisterProPR.Domain.Enums;
using MeisterProPR.Domain.ValueObjects;

namespace MeisterProPR.Infrastructure.Repositories;

/// <summary>
///     Thread-safe in-memory implementation of <see cref="IJobRepository" /> backed by a
///     <see cref="ConcurrentDictionary{TKey,TValue}" />. Used in non-DB mode and for tests.
/// </summary>
public sealed class InMemoryJobRepository : IJobRepository
{
    private readonly ConcurrentDictionary<Guid, ReviewJob> _jobs = new();

    /// <inheritdoc />
    public bool TryTransition(Guid id, JobStatus from, JobStatus to)
    {
        if (!this._jobs.TryGetValue(id, out var job))
        {
            return false;
        }

        lock (job)
        {
            if (job.Status != from)
            {
                return false;
            }

            job.Status = to;
            return true;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ReviewJob> GetAllForClient(Guid clientId)
    {
        return this._jobs.Values
            .Where(j => j.ClientId == clientId)
            .OrderByDescending(j => j.SubmittedAt)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ReviewJob> GetPendingJobs()
    {
        return this._jobs.Values
            .Where(j => j.Status == JobStatus.Pending)
            .OrderBy(j => j.SubmittedAt)
            .ToList();
    }

    /// <inheritdoc />
    public ReviewJob? FindActiveJob(
        string organizationUrl,
        string projectId,
        string repositoryId,
        int pullRequestId,
        int iterationId)
    {
        return this._jobs.Values.FirstOrDefault(j =>
            j.OrganizationUrl == organizationUrl &&
            j.ProjectId == projectId &&
            j.RepositoryId == repositoryId &&
            j.PullRequestId == pullRequestId &&
            j.IterationId == iterationId &&
            j.Status != JobStatus.Failed);
    }

    /// <inheritdoc />
    public ReviewJob? GetById(Guid id)
    {
        return this._jobs.GetValueOrDefault(id);
    }

    /// <inheritdoc />
    public Task<(int total, IReadOnlyList<ReviewJob> items)> GetAllJobsAsync(
        int limit,
        int offset,
        JobStatus? status,
        CancellationToken ct = default)
    {
        var query = this._jobs.Values.AsEnumerable();
        if (status.HasValue)
        {
            query = query.Where(j => j.Status == status.Value);
        }

        var ordered = query.OrderByDescending(j => j.SubmittedAt).ToList();
        var total = ordered.Count;
        var items = (IReadOnlyList<ReviewJob>)ordered.Skip(offset).Take(limit).ToList();
        return Task.FromResult((total, items));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReviewJob>> GetProcessingJobsAsync(CancellationToken ct = default)
    {
        var result = (IReadOnlyList<ReviewJob>)this._jobs.Values
            .Where(j => j.Status == JobStatus.Processing)
            .ToList();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public void Add(ReviewJob job)
    {
        this._jobs[job.Id] = job;
    }

    /// <inheritdoc />
    public void SetFailed(Guid id, string errorMessage)
    {
        if (this._jobs.TryGetValue(id, out var job))
        {
            lock (job)
            {
                job.ErrorMessage = errorMessage;
                job.Status = JobStatus.Failed;
                job.CompletedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <inheritdoc />
    public void SetResult(Guid id, ReviewResult result)
    {
        if (this._jobs.TryGetValue(id, out var job))
        {
            lock (job)
            {
                job.Result = result;
                job.Status = JobStatus.Completed;
                job.CompletedAt = DateTimeOffset.UtcNow;
            }
        }
    }
}
