using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Auth;

namespace Omnichannel.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        return services;
    }
}
