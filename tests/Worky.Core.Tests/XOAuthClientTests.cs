using Worky.Core.Auth;

namespace Worky.Core.Tests;

public class XOAuthClientTests
{
    [Fact]
    public void AuthorizeUrlCarriesPkceParameters()
    {
        var url = XOAuthClient.BuildAuthorizeUrl(
            "cid-123", "http://127.0.0.1:49152/callback", "state-x_1", "challenge-_1");

        Assert.StartsWith(XOAuthClient.AuthorizeEndpoint + "?", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("client_id=cid-123", url);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A49152%2Fcallback", url);
        Assert.Contains($"scope={Uri.EscapeDataString(XOAuthClient.Scopes)}", url);
        Assert.Contains("state=state-x_1", url);
        Assert.Contains("code_challenge=challenge-_1", url);
        Assert.Contains("code_challenge_method=S256", url);
    }

    [Fact]
    public void ScopeListMatchesTicketedMinimum()
    {
        Assert.Equal("tweet.read users.read follows.read offline.access", XOAuthClient.Scopes);
    }
}
