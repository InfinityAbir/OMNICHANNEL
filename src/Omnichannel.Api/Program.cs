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
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
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
builder.Services.AddAuthorization(options =>
{
    // SignalR hub connections must be authenticated with a valid tenant_id + sub claim.
    // The InboxHubAuthorizationHandler succeeds when the token carries a valid tenant_id.
    options.AddPolicy("RealtimeHub", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new HubAuthorizationRequirement());
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

app.MapHub<InboxHub>("/hubs/inbox");

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
