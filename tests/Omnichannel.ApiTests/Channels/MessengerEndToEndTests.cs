using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Omnichannel.ApiTests.Channels;

/// <summary>Mirrors WhatsApp/InstagramEndToEndTests — proves the real, production-registered MessengerChannelAdapter is correctly wired through Program.cs's DI and appsettings.Testing.json config.</summary>
public class MessengerEndToEndTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private const string AppSecret = "test-only-messenger-app-secret-never-used-outside-automated-tests";
    private const string VerifyToken = "test-only-messenger-verify-token";

    [Fact]
    public async Task GetHandshake_WithConfiguredVerifyToken_ReturnsChallenge()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(new Uri(
            $"/webhooks/messenger?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=112233", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("112233", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetHandshake_WithWrongVerifyToken_ReturnsForbidden()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(new Uri(
            "/webhooks/messenger?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=112233", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostWebhook_UnsignedPayload_ReturnsForbidden()
    {
        using var client = factory.CreateClient();
        var body = """{"object":"page","entry":[]}""";

        var response = await client.PostAsync(new Uri("/webhooks/messenger", UriKind.Relative), new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostWebhook_CorrectlySignedEmptyPayload_ReturnsOk()
    {
        using var client = factory.CreateClient();
        const string body = """{"object":"page","entry":[]}""";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        var signature = "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhooks/messenger", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Hub-Signature-256", signature);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
