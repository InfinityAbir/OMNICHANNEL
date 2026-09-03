using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Abstractions;
using Omnichannel.Infrastructure.Channels;
using Omnichannel.Infrastructure.Email;
using Omnichannel.Infrastructure.Identity;
using Omnichannel.Infrastructure.Persistence;
using Omnichannel.Infrastructure.Realtime;
using Omnichannel.Infrastructure.Widget;

namespace Omnichannel.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing required configuration: ConnectionStrings:Default");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.AddIdentityCore<ApplicationUser>(options =>
        {
            // Password policy (PRD §13): stronger than Identity's default minimum.
            options.Password.RequiredLength = 10;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;

            // Brute-force protection (PRD §13/§36).
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;

            options.User.RequireUniqueEmail = true;

            // Off by default per PRD §13 ("email verification if enabled") — a tenant/deployment
            // can turn this on; the confirmation email + endpoint work regardless of this flag.
            options.SignIn.RequireConfirmedEmail = configuration.GetValue("Identity:RequireConfirmedEmail", false);
        })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<WidgetTokenOptions>(configuration.GetSection(WidgetTokenOptions.SectionName));
        services.Configure<WidgetOptions>(configuration.GetSection(WidgetOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, ScopedTenantContext>();
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddSingleton<IAccessTokenGenerator, JwtAccessTokenGenerator>();
        services.AddSingleton<IWidgetSessionTokenGenerator, WidgetSessionTokenGenerator>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        // No IChannelAdapter is registered in production yet — Phase 7+ adds one per provider
        // (e.g. AddScoped<IChannelAdapter, WhatsAppChannelAdapter>()) as each channel ships
        // (PRD §65: "do not implement all providers at once"). The registry and pipeline below
        // work correctly with zero registered adapters — every real channel type just resolves
        // to null until then, and Manual/WebsiteChat were never meant to go through this path.
        // Scoped, not Singleton — a future adapter (Phase 7+) may itself be Scoped (e.g. needs a
        // per-request DbContext), and capturing it into a Singleton registry would be a captive
        // dependency. The registry is cheap to rebuild per scope either way.
        services.AddScoped<IChannelAdapterRegistry, ChannelAdapterRegistry>();
        services.AddScoped<IChannelCredentialStore, DataProtectionChannelCredentialStore>();

        services.AddSignalRNotifier();

        return services;
    }
}
