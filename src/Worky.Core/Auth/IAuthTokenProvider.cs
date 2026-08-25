namespace Worky.Core.Auth;

public interface IAuthTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken ct = default);
}

public sealed class StaticAuthTokenProvider(string token) : IAuthTokenProvider
{
    public Task<string> GetTokenAsync(CancellationToken ct = default) => Task.FromResult(token);
}
