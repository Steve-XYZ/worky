namespace Worky.Core.Auth;

public sealed class RefreshFailedException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public const string ReLoginHint = "Run 'worky login' again.";
}

public sealed class UserAuthTokenProvider(
    IAuthSessionStore store,
    IXTokenEndpoint tokenEndpoint,
    IClock clock) : IAuthTokenProvider
{
    static readonly TimeSpan Skew = TimeSpan.FromSeconds(30);

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        var session = store.Load()
            ?? throw new RefreshFailedException($"No stored X login found. {RefreshFailedException.ReLoginHint}");

        if (session.ExpiresAt > clock.UtcNow + Skew)
            return session.AccessToken;

        return await RefreshAsync(session, ct);
    }

    async Task<string> RefreshAsync(AuthSession session, CancellationToken ct)
    {
        TokenResponse response;
        try
        {
            response = await tokenEndpoint.RefreshAsync(session.RefreshToken, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RefreshFailedException(
                $"Refreshing your X login failed ({ex.Message}). {RefreshFailedException.ReLoginHint}", ex);
        }

        var updated = session with
        {
            AccessToken = response.AccessToken,
            RefreshToken = response.RefreshToken,
            ExpiresAt = clock.UtcNow.AddSeconds(response.ExpiresIn),
            Scope = response.Scope,
        };
        store.Save(updated);
        return updated.AccessToken;
    }
}
