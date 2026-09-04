using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Omnichannel.Api.Authorization;
using Omnichannel.Api.Endpoints;
using Omnichannel.Infrastructure.Realtime;
using Omnichannel.Api.Middleware;
using Omnichannel.Application;
using Omnichannel.Infrastructure;
using Omnichannel.Infrastructure.Identity;
using Omnichannel.Infrastructure.Persistence;
using Omnichannel.Infrastructure.Widget;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging (Serilog, structured, console sink; OTLP export handled by OpenTelemetry below) ----
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithCorrelationId()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

// ---- Observability (OpenTelemetry: traces + metrics; exporter is opt-in via OTEL_EXPORTER_OTLP_ENDPOINT) ----
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "Omnichannel.Api"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddHttpClientInstrumentation();
        metrics.AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    });

// ---- API versioning scaffold ----
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// ---- Problem-details error contract (no internal exception details leak to clients) ----
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

// ---- CORS: deny by default, explicit allowlist from configuration ----
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        }
    });
    // The widget embed is loaded by arbitrary customer sites and calls the public/widget endpoints
    // cross-origin with a bearer token (auth is never cookie-based). SignalR's negotiate fetch uses
    // credentials: 'include', so we echo the request origin and allow credentials rather than use the
    // wildcard '*' (which Chromium rejects for credentialed requests). Because the widget never trusts
    // cookies, allowing credentials here grants no additional privilege.
    options.AddPolicy("WidgetEmbed", policy =>
        policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});

// ---- Authentication: JWT bearer, signed with the key issued at login/refresh ----
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtSigningKey = jwtSection["SigningKey"]
    ?? throw new InvalidOperationException("Missing required configuration: Jwt:SigningKey");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, the JWT bearer handler silently remaps short claim names ("sub" etc.)
        // to long legacy XML-namespace URIs when building ClaimsPrincipal — breaking any code
        // (like ScopedTenantContext) that looks up JwtRegisteredClaimNames.Sub directly.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        // SignalR WebSockets cannot set an Authorization header, so the client sends the token
        // in the query string (?access_token=...) via accessTokenFactory. Read it here for hub
        // paths only — never accept query-string tokens on regular HTTP API calls.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Widget traffic is authenticated by the dedicated "Widget" scheme (below). Skip it
                // here so the default agent scheme doesn't emit noisy, misleading audience-validation
                // errors for widget tokens (widget audience != agent audience). NoResult() short-circuits
                // before token validation runs (the token may be present in the Authorization header).
                if (context.HttpContext.Request.Path.StartsWithSegments("/hubs/widget")
                    || context.HttpContext.Request.Path.StartsWithSegments("/widget"))
                {
                    context.NoResult();
                    return Task.CompletedTask;
                }

                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    })
    // Widget scheme: a separate JWT audience ("widget") + claim set, signed with the same key and
    // issuer as agent tokens (one issuer/key). Because the audience differs, a widget token can
    // never call agent APIs and an agent token can never drive the widget. A widget token carries
    // tenant_id + conversation_id + visitor_id, so ScopedTenantContext and the EF tenant filter
    // resolve correctly for widget-authenticated requests.
    .AddJwtBearer("Widget", options =>
    {
        options.MapInboundClaims = false;
        var widgetSection = builder.Configuration.GetSection(WidgetTokenOptions.SectionName);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = widgetSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(15),
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/widget"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

// ---- Authorization: permission-string policies resolved dynamically (PermissionKeys.*) ----
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, InboxHubAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, WidgetHubAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    // SignalR hub connections must be authenticated with a valid tenant_id + sub claim.
    // The InboxHubAuthorizationHandler succeeds when the token carries a valid tenant_id.
    options.AddPolicy("RealtimeHub", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new HubAuthorizationRequirement());
    });
    // Visitor-facing widget hub + widget API: authenticated via the "Widget" scheme with a valid
    // tenant_id + conversation_id (both come from the server-issued session token, never client input).
    options.AddPolicy("WidgetHub", policy =>
    {
        policy.AddAuthenticationSchemes("Widget");
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new WidgetHubAuthorizationRequirement());
    });
    options.AddPolicy("WidgetSession", policy =>
    {
        policy.AddAuthenticationSchemes("Widget");
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new WidgetHubAuthorizationRequirement());
    });
});

// ---- Rate limiting: brute-force protection on auth endpoints (PRD §13/§36) ----
// Secondary defense — Identity's account lockout (5 failed attempts / 15 min, see
// AddInfrastructure) is the primary one. This is deliberately generous per-IP (a shared
// office/NAT connection running many legitimate signups/logins shouldn't get blocked) while
// still meaningfully throttling distributed password-guessing.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // Spam/abuse control for the public widget endpoints (PRD §64): bounded fixed window per
    // remote IP. Generous enough for a real conversation while throttling scripted abuse.
    options.AddPolicy("widget", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // Provider webhooks (PRD §65): higher ceiling than the widget policy — a connected provider
    // can legitimately burst many events from a small set of known IPs, and rejecting a
    // legitimate delivery just means the provider retries it later, so this only needs to bound
    // gross abuse, not shape normal traffic.
    options.AddPolicy("webhook", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ---- SignalR ----
builder.Services.AddSignalR();

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("Default") ?? string.Empty,
        name: "postgres",
        tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSecurityHeaders();
// Serve the self-hosted widget embed assets (embed.js / widget.css) from wwwroot/widget. These are
// public, static, and contain no tenant data; the tenant-scoped logic all lives in the /widget API.
app.UseStaticFiles();
app.UseCors("Default");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.MapAuthEndpoints();
app.MapUsersEndpoints();
app.MapContactsEndpoints();
app.MapConversationsEndpoints();
app.MapTagsEndpoints();
app.MapAuditEndpoints();
app.MapWidgetEndpoints();
app.MapChannelWebhookEndpoints();
app.MapAiEndpoints();
app.MapKnowledgeEndpoints();
app.MapAutomationEndpoints();
app.MapNotificationEndpoints();
app.MapAnalyticsEndpoints();

app.MapHub<InboxHub>("/hubs/inbox");
app.MapHub<WidgetHub>("/hubs/widget").RequireCors("WidgetEmbed");

// Auto-migrate only in Development/Testing — production schema changes go through a
// deliberate, reviewed deploy step (AGENTS.md: migrations must be reviewable and tenant-safe),
// not applied implicitly on every process start. Role seeding is idempotent and always safe.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Testing")
    {
        await db.Database.MigrateAsync();
    }

    await RoleSeeder.SeedAsync(db, CancellationToken.None);
}

app.Run();
