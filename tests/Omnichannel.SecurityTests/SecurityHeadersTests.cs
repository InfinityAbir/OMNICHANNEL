using Microsoft.AspNetCore.Mvc.Testing;

namespace Omnichannel.SecurityTests;

/// <summary>
/// Regression coverage for the Phase 0 secure-headers baseline
/// (see Omnichannel.Api/Middleware/SecurityHeadersMiddleware.cs).
/// </summary>
public class SecurityHeadersTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Response_IncludesBaselineSecurityHeaders()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Server"));
    }
}
