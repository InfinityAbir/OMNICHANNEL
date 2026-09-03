using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Omnichannel.SecurityTests;

/// <summary>
/// Forces the "Testing" environment explicitly rather than relying on whatever
/// ASPNETCORE_ENVIRONMENT happens to be ambient on the machine running the tests — that
/// ambient dependency is exactly what let Phase 1/2 pass locally while silently failing in CI
/// (Program.cs's auto-migrate check never ran, and Jwt:SigningKey was only ever configured via
/// local `dotnet user-secrets`, which CI has no access to). "Testing" loads
/// appsettings.Testing.json, which carries a test-only, non-secret signing key and the same
/// dev-only connection string docker-compose/CI's Postgres service both use.
/// </summary>
public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.UseEnvironment("Testing");
}
