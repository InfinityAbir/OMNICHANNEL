using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Omnichannel.ApiTests.Channels;

/// <summary>
/// Exercises the real, production-registered WhatsAppChannelAdapter through the actual HTTP
/// pipeline (no fake adapter override) — proves Program.cs's DI wiring and appsettings.Testing.json
/// config are correct end-to-end, complementing WhatsAppChannelAdapterTests' isolated unit
/// coverage of the adapter's own logic.
/// </summary>
public class WhatsAppEndToEndTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private const string AppSecret = "test-only-whatsapp-app-secret-never-used-outside-automated-tests";
    private const string VerifyToken = "test-only-whatsapp-verify-token";

    [Fact]
    public async Task GetHandshake_WithConfiguredVerifyToken_ReturnsChallenge()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(new Uri(
            $"/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=999888", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("999888", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetHandshake_WithWrongVerifyToken_ReturnsForbidden()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(new Uri(
            "/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=999888", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostWebhook_UnsignedPayload_ReturnsForbiddenAndPersistsNothing()
    {
        using var client = factory.CreateClient();
        var body = JsonSerializer.Serialize(new { @object = "whatsapp_business_account", entry = Array.Empty<object>() });

        var response = await client.PostAsync(new Uri("/webhooks/whatsapp", UriKind.Relative), new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostWebhook_CorrectlySignedEmptyPayload_ReturnsOk()
    {
        using var client = factory.CreateClient();
        var body = """{"object":"whatsapp_business_account","entry":[]}""";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        var signature = "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhooks/whatsapp", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Hub-Signature-256", signature);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
