using MeisterProPR.Infrastructure.Data;
using MeisterProPR.Infrastructure.Data.Models;
using MeisterProPR.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace MeisterProPR.Infrastructure.Tests.Repositories;

/// <summary>
///     Integration tests for <see cref="PostgresClientRegistry" /> against a real PostgreSQL instance.
/// </summary>
[Collection("PostgresIntegration")]
public sealed class PostgresClientRegistryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private MeisterProPRDbContext _dbContext = null!;
    private PostgresClientRegistry _registry = null!;

    public async Task DisposeAsync()
    {
        await this._dbContext.DisposeAsync();
        await this._postgres.DisposeAsync();
    }

    // ── GetClientIdByKeyAsync (T046) ──────────────────────────────────────────

    [Fact]
    public async Task GetClientIdByKeyAsync_ReturnsGuid_ForValidActiveKey()
    {
        var record = await this.SeedClientAsync("active-key-get-id");
        var result = await this._registry.GetClientIdByKeyAsync("active-key-get-id");
        Assert.NotNull(result);
        Assert.Equal(record.Id, result.Value);
    }

    [Fact]
    public async Task GetClientIdByKeyAsync_ReturnsNull_ForEmpty()
    {
        var result = await this._registry.GetClientIdByKeyAsync("");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetClientIdByKeyAsync_ReturnsNull_ForInactiveKey()
    {
        await this.SeedClientAsync("inactive-get-id", false);
        var result = await this._registry.GetClientIdByKeyAsync("inactive-get-id");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetClientIdByKeyAsync_ReturnsNull_ForUnknownKey()
    {
        var result = await this._registry.GetClientIdByKeyAsync("not-in-database");
        Assert.Null(result);
    }

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        var options = new DbContextOptionsBuilder<MeisterProPRDbContext>()
            .UseNpgsql(this._postgres.GetConnectionString())
            .Options;
        this._dbContext = new MeisterProPRDbContext(options);
        await this._dbContext.Database.MigrateAsync();
        this._registry = new PostgresClientRegistry(this._dbContext, NullLogger<PostgresClientRegistry>.Instance);
    }

    [Fact]
    public async Task IsValidKey_ReturnsFalse_ForInactiveKey()
    {
        await this.SeedClientAsync("inactive-key-xyz", false);
        Assert.False(this._registry.IsValidKey("inactive-key-xyz"));
    }

    // ── IsValidKey ────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsValidKey_ReturnsTrue_ForActiveKey()
    {
        await this.SeedClientAsync("valid-key-abc123");
        Assert.True(this._registry.IsValidKey("valid-key-abc123"));
    }

    [Fact]
    public void IsValidKey_ReturnsFalse_ForEmpty()
    {
        Assert.False(this._registry.IsValidKey(""));
    }

    [Fact]
    public void IsValidKey_ReturnsFalse_ForUnknownKey()
    {
        Assert.False(this._registry.IsValidKey("totally-unknown-key"));
    }

    private async Task<ClientRecord> SeedClientAsync(string key, bool isActive = true)
    {
        var record = new ClientRecord
        {
            Id = Guid.NewGuid(),
            Key = key,
            DisplayName = "Test Client",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        this._dbContext.Clients.Add(record);
        await this._dbContext.SaveChangesAsync();
        return record;
    }
}