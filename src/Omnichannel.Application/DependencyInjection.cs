using Microsoft.Extensions.DependencyInjection;

namespace Omnichannel.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // No use cases yet — Phase 1 adds the first application services
        // (auth, tenant provisioning) here.
        return services;
    }
}
