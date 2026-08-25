using System.Text.Json.Serialization;

namespace Worky.Core.Auth;

public sealed record AuthSession(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("username")] string UserName);
