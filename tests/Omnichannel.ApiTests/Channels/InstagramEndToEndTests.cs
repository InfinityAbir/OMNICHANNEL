using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Omnichannel.ApiTests.Channels;

/// <summary>Mirrors WhatsAppEndToEndTests — proves the real, production-registered InstagramChannelAdapter is correctly wired through Program.cs's DI and appsettings.Testing.json config.</summary>
public class InstagramEndToEndTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private const string AppSecret = "test-only-instagram-app-secret-never-used-outside-automated-tests";
    private const string VerifyToken = "test-only-instagram-verify-token";

    [Fact]
    public async Task GetHandshake_WithConfiguredVerifyToken_ReturnsChallenge()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(new Uri(
            $"/webhooks/instagram?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=777666", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("777666", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetHandshake_WithWrongVerifyToken_ReturnsForbidden()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(new Uri(
            "/webhooks/instagram?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=777666", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostWebhook_UnsignedPayload_ReturnsForbidden()
    {
        using var client = factory.CreateClient();
        var body = """{"object":"instagram","entry":[]}""";

        var response = await client.PostAsync(new Uri("/webhooks/instagram", UriKind.Relative), new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostWebhook_CorrectlySignedEmptyPayload_ReturnsOk()
    {
        using var client = factory.CreateClient();
        const string body = """{"object":"instagram","entry":[]}""";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        var signature = "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhooks/instagram", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Hub-Signature-256", signature);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
