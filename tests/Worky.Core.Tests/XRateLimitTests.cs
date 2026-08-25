using System.Net;
using System.Text;
using Worky.Core.Auth;

namespace Worky.Core.Tests;

public class XRateLimitTests
{
    sealed class SingleResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(response);
        }
    }

    static HttpResponseMessage RateLimited(string? resetHeader, HttpStatusCode status = HttpStatusCode.TooManyRequests)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"title":"Too Many Requests"}""", Encoding.UTF8, "application/json"),
        };
        if (resetHeader is not null)
            response.Headers.TryAddWithoutValidation("x-rate-limit-reset", resetHeader);
        return response;
    }

    static XApiClient Client(HttpResponseMessage response, out SingleResponseHandler handler)
    {
        handler = new SingleResponseHandler(response);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.x.com/2/") };
        return new XApiClient(http, new StaticAuthTokenProvider("tok"));
    }

    [Fact]
    public async Task ValidEpochHeaderCarriesResetAtAndEndpoint()
    {
        var client = Client(RateLimited("1787600000"), out _);

        var ex = await Assert.ThrowsAsync<XRateLimitException>(
            () => client.SearchRecentAsync("hiring", 10));

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal("tweets/search/recent", ex.Endpoint);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1787600000), ex.ResetAt);
        Assert.Equal(TimeSpan.Zero, ex.ResetAt!.Value.Offset);
    }

    [Fact]
    public async Task MissingResetHeaderYieldsNullResetAt()
    {
        var client = Client(RateLimited(null), out _);

        var ex = await Assert.ThrowsAsync<XRateLimitException>(
            () => client.SearchRecentAsync("hiring", 10));

        Assert.Equal(429, ex.StatusCode);
        Assert.Null(ex.ResetAt);
        Assert.Equal("tweets/search/recent", ex.Endpoint);
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("12.5")]
    [InlineData("")]
    public async Task GarbageResetHeaderYieldsNullResetAt(string header)
    {
        var client = Client(RateLimited(header), out _);

        var ex = await Assert.ThrowsAsync<XRateLimitException>(
            () => client.SearchRecentAsync("hiring", 10));

        Assert.Null(ex.ResetAt);
    }

    [Fact]
    public async Task FollowingRateLimitCarriesFollowingEndpoint()
    {
        var client = Client(RateLimited("1787600000"), out _);

        var ex = await Assert.ThrowsAsync<XRateLimitException>(
            () => client.GetFollowingAsync("me-1", maxPages: 1));

        Assert.Equal("users/me-1/following", ex.Endpoint);
        Assert.NotNull(ex.ResetAt);
    }

    [Fact]
    public async Task NonRateLimitErrorsRemainPlainXApiException()
    {
        var client = Client(RateLimited("1787600000", HttpStatusCode.InternalServerError), out var handler);

        var ex = await Assert.ThrowsAsync<XApiException>(
            () => client.GetMeAsync());

        Assert.IsNotType<XRateLimitException>(ex);
        Assert.Equal(500, ex.StatusCode);
        Assert.Equal(1, handler.Requests);
    }
}
