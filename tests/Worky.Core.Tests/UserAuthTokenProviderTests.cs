using System.Net.Http;
using Worky.Core.Auth;

namespace Worky.Core.Tests;

public class UserAuthTokenProviderTests
{
    static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    sealed class FakeStore : IAuthSessionStore
    {
        public AuthSession? Session { get; set; }
        public int SaveCount { get; private set; }

        public AuthSession? Load() => Session;

        public void Save(AuthSession session)
        {
            SaveCount++;
            Session = session;
        }
    }

    sealed class FakeTokenEndpoint : IXTokenEndpoint
    {
        public string? LastRefreshToken { get; private set; }
        public int RefreshCount { get; private set; }
        public Exception? ThrowOnRefresh { get; set; }
        public Func<TokenResponse> Responder { get; set; } =
            () => new TokenResponse("fresh-access", "next-refresh", 7200, "tweet.read");

        public Task<TokenResponse> ExchangeAuthorizationCodeAsync(
            string code, string codeVerifier, string redirectUri, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
        {
            RefreshCount++;
            LastRefreshToken = refreshToken;
            if (ThrowOnRefresh is not null) return Task.FromException<TokenResponse>(ThrowOnRefresh);
            return Task.FromResult(Responder());
        }
    }

    static (UserAuthTokenProvider Provider, FakeStore Store, FakeTokenEndpoint Endpoint) Build(
        AuthSession? session)
    {
        var store = new FakeStore { Session = session };
        var endpoint = new FakeTokenEndpoint();
        var provider = new UserAuthTokenProvider(store, endpoint, new FakeClock(Now));
        return (provider, store, endpoint);
    }

    static AuthSession SessionExpiring(DateTimeOffset expiresAt) => new()
    {
        AccessToken = "stale-access",
        RefreshToken = "rotating-refresh",
        ExpiresAt = expiresAt,
        Scope = "tweet.read users.read",
        UserId = "u-1",
        UserName = "alice",
    };

    [Fact]
    public async Task ReturnsStoredAccessTokenWithoutRefreshingWhenFresh()
    {
        var (provider, store, endpoint) = Build(SessionExpiring(Now.AddSeconds(31)));

        Assert.Equal("stale-access", await provider.GetTokenAsync());
        Assert.Equal(0, endpoint.RefreshCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RefreshesWhenExpiringInsideSkewBuffer()
    {
        var (provider, store, endpoint) = Build(SessionExpiring(Now.AddSeconds(30)));

        Assert.Equal("fresh-access", await provider.GetTokenAsync());

        Assert.Equal("rotating-refresh", endpoint.LastRefreshToken);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal("fresh-access", store.Session!.AccessToken);
        Assert.Equal("next-refresh", store.Session!.RefreshToken);
        Assert.Equal(Now.AddSeconds(7200), store.Session!.ExpiresAt);
    }

    [Fact]
    public async Task RefreshesExactlyOnceWhenAlreadyExpired()
    {
        var (provider, _, endpoint) = Build(SessionExpiring(Now.AddMinutes(-1)));

        Assert.Equal("fresh-access", await provider.GetTokenAsync());
        Assert.Equal(1, endpoint.RefreshCount);
    }

    [Fact]
    public async Task FailsWithoutRetryOrOverwriteWhenRefreshRejected()
    {
        var (provider, store, endpoint) = Build(SessionExpiring(Now.AddMinutes(-1)));
        endpoint.ThrowOnRefresh = new HttpRequestException("X API returned 503: upstream");

        var ex = await Assert.ThrowsAsync<RefreshFailedException>(() => provider.GetTokenAsync());

        Assert.Contains(RefreshFailedException.ReLoginHint, ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);
        Assert.Equal(1, endpoint.RefreshCount);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal("rotating-refresh", store.Session!.RefreshToken);
    }

    [Fact]
    public async Task MissingStoredSessionPointsAtLogin()
    {
        var (provider, _, endpoint) = Build(null);

        var ex = await Assert.ThrowsAsync<RefreshFailedException>(() => provider.GetTokenAsync());

        Assert.Contains(RefreshFailedException.ReLoginHint, ex.Message);
        Assert.Equal(0, endpoint.RefreshCount);
    }

    [Fact]
    public async Task ConcurrentCallsShareASingleRotation()
    {
        var store = new FakeStore { Session = SessionExpiring(Now.AddMinutes(-1)) };
        var endpoint = new FakeTokenEndpoint();
        var provider = new UserAuthTokenProvider(store, endpoint, new FakeClock(Now));

        var tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => provider.GetTokenAsync()));

        Assert.Equal(1, endpoint.RefreshCount);
        Assert.All(tokens, t => Assert.Equal("fresh-access", t));
        Assert.Equal("next-refresh", store.Session!.RefreshToken);
        Assert.Equal(1, store.SaveCount);
    }
}
