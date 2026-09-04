using Microsoft.EntityFrameworkCore;
using Omnichannel.Application.Abstractions;
using Omnichannel.Infrastructure.Persistence;
using Pgvector.EntityFrameworkCore;

namespace Omnichannel.IntegrationTests;

/// <summary>
/// Proves the EF Core + Npgsql wiring can reach a real PostgreSQL instance.
/// Requires the docker-compose Postgres service (see docker-compose.yml) or an
/// equivalent connection string in the OMNICHANNEL_TEST_CONNECTION env var.
/// </summary>
public class AppDbContextConnectivityTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("OMNICHANNEL_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=omnichannel;Username=omnichannel;Password=omnichannel_dev_only";

    [Fact]
    public async Task CanConnect_ToConfiguredPostgres()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, o => o.UseVector())
            .Options;

        await using var context = new AppDbContext(options, new UnauthenticatedTenantContext());

        var canConnect = await context.Database.CanConnectAsync();

        Assert.True(canConnect, "Expected to reach PostgreSQL via docker-compose (run 'docker compose up -d postgres' first).");
    }
}

/// <summary>Test double for contexts constructed outside an HTTP request.</summary>
public sealed class UnauthenticatedTenantContext : ITenantContext
{
    public bool IsAuthenticated => false;

    public Guid TenantId => Guid.Empty;

    public Guid UserId => Guid.Empty;
}
