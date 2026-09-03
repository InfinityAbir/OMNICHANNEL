using System.Net.Http.Headers;
using System.Net.Http.Json;
using Omnichannel.Contracts.Auth;

namespace Omnichannel.ApiTests;

internal static class TestAuth
{
    public static async Task<string> RegisterAndGetAccessTokenAsync(HttpClient client, string? businessName = null)
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/v1/auth/register", UriKind.Relative), new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.test",
            Password = "Str0ng!Passw0rd",
            DisplayName = "Test Owner",
            BusinessName = businessName ?? $"Test Business {Guid.NewGuid():N}",
        });

        var tokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        return tokens!.AccessToken;
    }

    public static void UseBearer(this HttpClient client, string accessToken)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
}
