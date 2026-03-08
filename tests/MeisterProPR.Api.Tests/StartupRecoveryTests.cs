using MeisterProPR.Application.Interfaces;
using MeisterProPR.Domain.Entities;
using MeisterProPR.Domain.Enums;
using MeisterProPR.Infrastructure.Data;
using MeisterProPR.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace MeisterProPR.Api.Tests;

/// <summary>
///     Integration tests verifying startup recovery (A3 fix):
///     stale <see cref="JobStatus.Processing" /> jobs present in the database at startup
///     must be reset to <see cref="JobStatus.Pending" /> by the startup recovery logic in
///     <c>Program.cs</c> before the application starts serving requests.
/// </summary>
[Collection("StartupRecoveryIntegration")]
public sealed class StartupRecoveryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public async Task DisposeAsync()
    {
        await this._postgres.DisposeAsync();
    }

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
    }

    /// <summary>
    ///     Directly inserts a <see cref="JobStatus.Processing" /> job into the database
    ///     (bypassing the app, simulating a job left over from a previous crash), then
    ///     starts a fresh <see cref="WebApplicationFactory{TEntryPoint}" /> pointing at the
    ///     same database. Asserts the job is now <see cref="JobStatus.Pending" /> after startup.
    /// </summary>
    [Fact]
    public async Task Startup_ProcessingJobInDatabase_TransitionsJobToPending()
    {
        var connectionString = this._postgres.GetConnectionString();

        // Step 1 — prepare DB and seed a stale Processing job directly (pre-restart)
        var options = new DbContextOptionsBuilder<MeisterProPRDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        Guid stalJobId;
        await using (var db = new MeisterProPRDbContext(options))
        {
            await db.Database.MigrateAsync();
            var repo = new PostgresJobRepository(db);
            var job = new ReviewJob(
                Guid.NewGuid(),
                null,
                "https://dev.azure.com/org",
                "proj",
                "repo",
                99,
                1);
            repo.Add(job);
            repo.TryTransition(job.Id, JobStatus.Pending, JobStatus.Processing);
            stalJobId = job.Id;
        }

        // Step 2 — start the application (simulates service restart)
        //          Startup recovery in Program.cs should transition the stale job to Pending.
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("DB_CONNECTION_STRING", connectionString);
                builder.UseSetting("AI_ENDPOINT", "https://fake.openai.azure.com/");
                builder.UseSetting("AI_DEPLOYMENT", "gpt-4o");
                builder.UseSetting("MEISTER_ADMIN_KEY", "admin-key-min-16-chars-ok");
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(Substitute.For<IAdoTokenValidator>());
                    services.AddSingleton(Substitute.For<IPullRequestFetcher>());
                    services.AddSingleton(Substitute.For<IAdoCommentPoster>());
                    services.AddSingleton(Substitute.For<IAssignedPullRequestFetcher>());
                });
            });

        _ = factory.CreateClient(); // triggers startup: migrations, bootstrap, recovery

        // Step 3 — assert the stale job is now Pending
        using var scope = factory.Services.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var recoveredJob = jobs.GetById(stalJobId);

        Assert.NotNull(recoveredJob);
        Assert.Equal(JobStatus.Pending, recoveredJob.Status);
    }
}