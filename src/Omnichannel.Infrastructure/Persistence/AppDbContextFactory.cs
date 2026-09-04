using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Omnichannel.Application.Abstractions;
using Pgvector.EntityFrameworkCore;

namespace Omnichannel.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef migrations add/remove` at design time — never by the running app
/// (Program.cs constructs AppDbContext through DI, with the real ITenantContext).
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("OMNICHANNEL_TEST_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=omnichannel;Username=omnichannel;Password=omnichannel_dev_only";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, o => o.UseVector())
            .Options;

        return new AppDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public bool IsAuthenticated => false;

        public Guid TenantId => Guid.Empty;

        public Guid UserId => Guid.Empty;
    }
}
