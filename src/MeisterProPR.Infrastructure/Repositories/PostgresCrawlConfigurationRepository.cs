using MeisterProPR.Application.DTOs;
using MeisterProPR.Application.Interfaces;
using MeisterProPR.Infrastructure.Data;
using MeisterProPR.Infrastructure.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MeisterProPR.Infrastructure.Repositories;

/// <summary>Database-backed crawl configuration repository.</summary>
public sealed class PostgresCrawlConfigurationRepository(MeisterProPRDbContext dbContext)
    : ICrawlConfigurationRepository
{
    /// <inheritdoc />
    public async Task<bool> SetActiveAsync(Guid configId, Guid clientId, bool isActive, CancellationToken ct = default)
    {
        var record = await dbContext.CrawlConfigurations
            .FirstOrDefaultAsync(c => c.Id == configId && c.ClientId == clientId, ct);
        if (record is null)
        {
            return false;
        }

        record.IsActive = isActive;
        await dbContext.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task<CrawlConfigurationDto> AddAsync(
        Guid clientId,
        string organizationUrl,
        string projectId,
        Guid reviewerId,
        int crawlIntervalSeconds,
        CancellationToken ct = default)
    {
        var record = new CrawlConfigurationRecord
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            OrganizationUrl = organizationUrl,
            ProjectId = projectId,
            ReviewerId = reviewerId,
            CrawlIntervalSeconds = crawlIntervalSeconds,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.CrawlConfigurations.Add(record);
        await dbContext.SaveChangesAsync(ct);
        return new CrawlConfigurationDto(
            record.Id,
            record.ClientId,
            record.OrganizationUrl,
            record.ProjectId,
            record.ReviewerId,
            record.CrawlIntervalSeconds,
            record.IsActive,
            record.CreatedAt);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CrawlConfigurationDto>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await dbContext.CrawlConfigurations
            .Where(c => c.IsActive)
            .Select(c => new CrawlConfigurationDto(
                c.Id,
                c.ClientId,
                c.OrganizationUrl,
                c.ProjectId,
                c.ReviewerId,
                c.CrawlIntervalSeconds,
                c.IsActive,
                c.CreatedAt))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CrawlConfigurationDto>> GetByClientAsync(
        Guid clientId,
        CancellationToken ct = default)
    {
        return await dbContext.CrawlConfigurations
            .Where(c => c.ClientId == clientId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CrawlConfigurationDto(
                c.Id,
                c.ClientId,
                c.OrganizationUrl,
                c.ProjectId,
                c.ReviewerId,
                c.CrawlIntervalSeconds,
                c.IsActive,
                c.CreatedAt))
            .ToListAsync(ct);
    }
}