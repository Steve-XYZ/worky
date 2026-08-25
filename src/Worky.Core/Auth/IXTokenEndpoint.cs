namespace Worky.Core.Auth;

public sealed record TokenResponse(string AccessToken, string RefreshToken, long ExpiresIn, string Scope);

public interface IXTokenEndpoint
{
    Task<TokenResponse> ExchangeAuthorizationCodeAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct = default);

    Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken ct = default);
}
