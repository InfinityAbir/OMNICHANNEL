using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Omnichannel.ApiTests;

public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Live_ReturnsHealthy_WithoutTouchingDependencies()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails_NotStackTrace()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/this-route-does-not-exist", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Omnichannel.Api", body, StringComparison.Ordinal);
    }
}
