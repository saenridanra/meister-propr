using MeisterProPR.Application.DTOs;
using MeisterProPR.Infrastructure.Repositories;

namespace MeisterProPR.Infrastructure.Tests.Repositories;

/// <summary>Unit tests for <see cref="NullClientAdoCredentialRepository" />.</summary>
public sealed class NullClientAdoCredentialRepositoryTests
{
    private readonly NullClientAdoCredentialRepository _sut = new();

    [Fact]
    public async Task GetByClientIdAsync_AlwaysReturnsNull()
    {
        var result = await _sut.GetByClientIdAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertAsync_IsNoOp_DoesNotThrow()
    {
        // Should complete without throwing
        await _sut.UpsertAsync(
            Guid.NewGuid(),
            new ClientAdoCredentials("tenant", "client", "secret"),
            CancellationToken.None);
    }

    [Fact]
    public async Task ClearAsync_IsNoOp_DoesNotThrow()
    {
        // Should complete without throwing
        await _sut.ClearAsync(Guid.NewGuid(), CancellationToken.None);
    }

    [Fact]
    public async Task GetByClientIdAsync_AfterUpsert_StillReturnsNull()
    {
        var clientId = Guid.NewGuid();
        await _sut.UpsertAsync(clientId, new ClientAdoCredentials("t", "c", "s"), CancellationToken.None);

        var result = await _sut.GetByClientIdAsync(clientId, CancellationToken.None);
        Assert.Null(result);
    }
}
