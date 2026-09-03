using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Infrastructure.Persistence;

namespace Omnichannel.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing required configuration: ConnectionStrings:Default");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }
}
