using System.Globalization;
using Asp.Versioning;
using Omnichannel.Api.Middleware;
using Omnichannel.Application;
using Omnichannel.Infrastructure;
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

// ---- API versioning scaffold (no versioned endpoints yet — first lands in Phase 1) ----
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

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

app.Run();
