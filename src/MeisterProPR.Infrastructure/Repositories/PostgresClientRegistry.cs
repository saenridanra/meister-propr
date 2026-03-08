using MeisterProPR.Application.Interfaces;
using MeisterProPR.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MeisterProPR.Infrastructure.Repositories;

/// <summary>Database-backed client registry using PostgreSQL.</summary>
public sealed class PostgresClientRegistry(
    MeisterProPRDbContext dbContext,
    ILogger<PostgresClientRegistry> logger) : IClientRegistry
{
    /// <inheritdoc />
    public bool IsValidKey(string clientKey)
    {
        if (string.IsNullOrWhiteSpace(clientKey))
        {
            return false;
        }

        return dbContext.Clients.Any(c => c.Key == clientKey && c.IsActive);
    }

    /// <inheritdoc />
    public async Task<Guid?> GetClientIdByKeyAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var client = await dbContext.Clients
            .Where(c => c.Key == key && c.IsActive)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        return client;
    }
}