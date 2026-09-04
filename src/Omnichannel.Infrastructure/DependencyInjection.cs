using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Omnichannel.Application.Abstractions;
using Omnichannel.Infrastructure.Ai;
using Omnichannel.Infrastructure.Channels;
using Omnichannel.Infrastructure.Email;
using Omnichannel.Infrastructure.Identity;
using Omnichannel.Infrastructure.Knowledge;
using Omnichannel.Infrastructure.Persistence;
using Omnichannel.Infrastructure.Realtime;
using Omnichannel.Infrastructure.Security;
using Pgvector.EntityFrameworkCore;
using Omnichannel.Infrastructure.Widget;

namespace Omnichannel.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing required configuration: ConnectionStrings:Default");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString, o => o.UseVector()));

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
        services.Configure<WhatsAppOptions>(configuration.GetSection(WhatsAppOptions.SectionName));
        services.Configure<InstagramOptions>(configuration.GetSection(InstagramOptions.SectionName));
        services.Configure<MessengerOptions>(configuration.GetSection(MessengerOptions.SectionName));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, ScopedTenantContext>();
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddSingleton<IAccessTokenGenerator, JwtAccessTokenGenerator>();
        services.AddSingleton<IWidgetSessionTokenGenerator, WidgetSessionTokenGenerator>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<ITenantSecretStore, DataProtectionTenantSecretStore>();

        // Data Protection's key ring persisted in Postgres, not the container's local filesystem
        // — required on any platform with an ephemeral filesystem (Render included), since the
        // default file-system persistence would silently mint a fresh key ring on every redeploy
        // and permanently strand every previously-encrypted TenantSecret/ChannelCredential value.
        // ApplicationName is pinned to a fixed literal (never derived from anything that could
        // change between deploys) — Data Protection scopes keys to it, so an accidental change
        // would have the exact same effect as losing the key ring outright.
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
            new ConfigureOptions<KeyManagementOptions>(options =>
                options.XmlRepository = new EfXmlRepository(sp.GetRequiredService<IServiceScopeFactory>())));
        services.AddDataProtection().SetApplicationName("Omnichannel");

        // Scoped, not Singleton — a future adapter may itself be Scoped (e.g. needs a per-request
        // DbContext), and capturing it into a Singleton registry would be a captive dependency.
        // The registry is cheap to rebuild per scope either way.
        services.AddScoped<IChannelAdapterRegistry, ChannelAdapterRegistry>();
        services.AddScoped<IChannelCredentialStore, DataProtectionChannelCredentialStore>();

        // Phase 7 (WhatsApp), Phase 8 (Instagram), Phase 9 (Messenger) adapters.
        services.AddHttpClient<IChannelAdapter, WhatsAppChannelAdapter>();
        services.AddHttpClient<IChannelAdapter, InstagramChannelAdapter>();
        services.AddHttpClient<IChannelAdapter, MessengerChannelAdapter>();

        services.AddSignalRNotifier();

        services.AddHttpClient<IAiProvider, GroqAiProvider>();
        services.AddHttpClient("tenant-ai-provider");
        services.AddScoped<IAiProviderResolver, AiProviderResolver>();
        services.AddScoped<IAiProviderDetector, AiProviderDetector>();
        services.AddScoped<IAiUsageLimiter, AiUsageLimiter>();

        // No embeddings-capable API key was available this phase (Groq's own catalog has none —
        // ADR-0021); a deterministic lexical embedding proves the retrieval pipeline for real
        // now, swappable for a neural provider later via this one registration.
        services.AddSingleton<IEmbeddingProvider, HashingEmbeddingProvider>();
        services.AddScoped<IKnowledgeRetrievalService, PgVectorKnowledgeRetrievalService>();

        return services;
    }
}
