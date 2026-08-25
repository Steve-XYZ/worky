using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Worky.Core;

namespace Worky.Core.Auth;

public sealed class XOAuthClient(HttpClient http, string clientId) : IXTokenEndpoint
{
    public const string AuthorizeEndpoint = "https://x.com/i/oauth2/authorize";
    public const string TokenEndpoint = "https://api.x.com/2/oauth2/token";
    public const string Scopes = "tweet.read users.read follows.read offline.access";

    public static string BuildAuthorizeUrl(string clientId, string redirectUri, string state, string codeChallenge)
    {
        var qs = new List<string>
        {
            "response_type=code",
            $"client_id={Uri.EscapeDataString(clientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"scope={Uri.EscapeDataString(Scopes)}",
            $"state={Uri.EscapeDataString(state)}",
            $"code_challenge={Uri.EscapeDataString(codeChallenge)}",
            "code_challenge_method=S256",
        };
        return $"{AuthorizeEndpoint}?{string.Join("&", qs)}";
    }

    public async Task<TokenResponse> ExchangeAuthorizationCodeAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier,
        };
        return await SendAsync(form, ct);
    }

    public async Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        };
        return await SendAsync(form, ct);
    }

    async Task<TokenResponse> SendAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
        if (!response.IsSuccessStatusCode)
            throw new XApiException((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));

        var payload = await response.Content.ReadFromJsonAsync<TokenResponseDto>(ApiJson.Options, ct);
        if (payload?.AccessToken is null || payload.RefreshToken is null || payload.ExpiresIn is null || payload.Scope is null)
            throw new XApiException((int)response.StatusCode, "incomplete token response");

        return new TokenResponse(payload.AccessToken, payload.RefreshToken, payload.ExpiresIn.Value, payload.Scope);
    }
}

internal sealed record TokenResponseDto(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] long? ExpiresIn,
    [property: JsonPropertyName("scope")] string? Scope);
