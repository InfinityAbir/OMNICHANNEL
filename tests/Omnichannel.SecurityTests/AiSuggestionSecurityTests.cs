using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Omnichannel.Application.Abstractions;
using Omnichannel.Contracts.Auth;
using Omnichannel.Contracts.Conversations;

namespace Omnichannel.SecurityTests;

/// <summary>PRD §69's security focus, the cross-tenant-leakage half: a tenant must never be able to generate (or thereby read) an AI suggestion for another tenant's conversation.</summary>
public class AiSuggestionSecurityTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private sealed class RecordingFakeAiProvider : IAiProvider
    {
        public AiPromptContext? LastContext { get; private set; }

        public Task<AiCompletionResult> GenerateSuggestionAsync(AiPromptContext context, CancellationToken cancellationToken)
        {
            LastContext = context;
            return Task.FromResult(new AiCompletionResult("suggestion", 0.5, "fake", 1, 1));
        }
    }

    [Fact]
    public async Task GenerateSuggestion_CannotReachAnotherTenantsConversation()
    {
        var fakeProvider = new RecordingFakeAiProvider();
        using var customFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IAiProvider>(fakeProvider)));

        using var tenantA = customFactory.CreateClient();
        tenantA.UseBearer(await RegisterAsync(tenantA));
        var createResponse = await tenantA.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative),
            new CreateConversationRequest { NewContactDisplayName = "Tenant A Customer", InitialMessageText = "Tenant A secret context" });
        var conversation = await createResponse.Content.ReadFromJsonAsync<ConversationDetailResponse>();

        using var tenantB = customFactory.CreateClient();
        tenantB.UseBearer(await RegisterAsync(tenantB));

        var response = await tenantB.PostAsync(new Uri($"/api/v1/conversations/{conversation!.Id}/ai-suggestions", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(fakeProvider.LastContext);
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.test",
            Password = "Str0ng!Passw0rd",
            DisplayName = "Test Owner",
            BusinessName = $"Test Business {Guid.NewGuid():N}",
        });
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        return tokens!.AccessToken;
    }
}

file static class HttpClientExtensions4
{
    public static void UseBearer(this HttpClient client, string accessToken)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
}
