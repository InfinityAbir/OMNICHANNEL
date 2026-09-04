using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
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

// ---- Forwarded headers: required behind Render's (or any) TLS-terminating reverse proxy ----
// Without this, Kestrel sees every request as plain HTTP (the proxy talks HTTP to the
// container), so UseHttpsRedirection/UseHsts below would redirect-loop real HTTPS traffic
// forever. KnownNetworks/KnownProxies are cleared because the proxy's IP isn't a fixed,
// allowlist-able address on a platform like Render — the header is trusted by topology (the
// container only ever receives traffic from the platform's own edge), the same trust model
// Render's own docs assume for ASP.NET Core apps.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

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
var jwtSigningKey = jwtSection["SigningKey"];
if (string.IsNullOrWhiteSpace(jwtSigningKey))
{
    // An empty (not just missing) value previously passed this check (empty string isn't null),
    // then failed confusingly later — SymmetricSecurityKey rejects a zero-length key only when
    // JwtBearerOptions is first lazily resolved, i.e. on the first authenticated request, as an
    // unhandled 500 instead of a clear startup failure. Fail fast here instead.
    throw new InvalidOperationException("Missing required configuration: Jwt:SigningKey");
}

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

    // Phase 15 hardening: every other policy below covers a specific unauthenticated surface
    // (auth/widget/webhook), but the general authenticated API (conversations, ai, automation,
    // analytics, knowledge, ...) had no bound at all — a compromised or scripted client could
    // otherwise hammer the DB or, worse, the paid AI provider without limit. Partitioned per
    // authenticated user (falling back to IP for anything unauthenticated, which the specific
    // policies above already cover more tightly), generous enough that no legitimate dashboard
    // usage pattern comes close to it.
    options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.IsAuthenticated == true
                ? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? context.User.FindFirst("sub")?.Value
                    ?? "authenticated-unknown"
                : context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

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

app.UseForwardedHeaders();
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
app.MapEmailSettingsEndpoints();

app.MapHub<InboxHub>("/hubs/inbox");
app.MapHub<WidgetHub>("/hubs/widget").RequireCors("WidgetEmbed");

// Serves the built Angular SPA (copied into wwwroot at Docker build time — see repo-root
// Dockerfile) for any GET that doesn't match an API/hub route above, so a single Render web
// service can host both the API and the frontend on one origin (no cross-origin CORS/SignalR
// config needed between them). Absent in local dev (wwwroot has no Angular build; `ng serve`'s
// own proxy handles that instead), so this silently does nothing outside a real deploy.
app.MapFallbackToFile("index.html");

// Auto-migrate in Development/Testing always; in any other environment only when explicitly
// opted in via RunMigrationsOnStartup (e.g. set true for a Render deploy — a hosted platform
// has no interactive terminal to run `dotnet ef database update` from, unlike a reviewed local/
// CI step). Production schema changes are otherwise a deliberate, reviewed step (AGENTS.md:
// migrations must be reviewable and tenant-safe), not implicit on every process start — the
// opt-in keeps that intent instead of blanket-enabling it for every non-dev environment.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var autoMigrate = app.Environment.IsDevelopment()
        || app.Environment.EnvironmentName == "Testing"
        || builder.Configuration.GetValue<bool>("RunMigrationsOnStartup");
    if (autoMigrate)
    {
        await db.Database.MigrateAsync();
    }

    await RoleSeeder.SeedAsync(db, CancellationToken.None);
}

app.Run();
