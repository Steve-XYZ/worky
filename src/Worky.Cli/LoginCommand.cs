using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Worky.Core;
using Worky.Core.Auth;

namespace Worky.Cli;

public static class LoginCommand
{
    public static async Task<int> RunAsync(CancellationToken ct = default)
    {
        var clientId = Environment.GetEnvironmentVariable("WORKY_CLIENT_ID");
        if (string.IsNullOrWhiteSpace(clientId))
        {
            Console.Error.WriteLine(
                "Set WORKY_CLIENT_ID to your X app's OAuth 2.0 public client id (User authentication settings).");
            return 2;
        }

        var verifier = Pkce.GenerateVerifier();
        var state = Pkce.CreateState();

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = $"http://127.0.0.1:{port}/callback";

        var authorizeUrl = XOAuthClient.BuildAuthorizeUrl(
            clientId, redirectUri, state, Pkce.CreateChallenge(verifier));
        Console.WriteLine("Opening X authorization in your browser...");
        Console.WriteLine(authorizeUrl);
        TryOpenBrowser(authorizeUrl);
        Console.WriteLine("Waiting for the callback...");

        string? code;
        var callback = await LoginCallback.AwaitAsync(listener, state, ct);
        if (callback.Error is not null)
        {
            Console.Error.WriteLine(callback.Error switch
            {
                LoginCallback.TimeoutError =>
                    "Timed out waiting for the X authorization callback; run 'worky login' to try again.",
                LoginCallback.TooManyConnectionsError =>
                    "Too many stray connections arrived on the callback port; run 'worky login' to try again.",
                _ => $"X reported an authorization error: {callback.Error}",
            });
            PrintRedirectHint(redirectUri);
            return 1;
        }
        if (callback.Code is null)
        {
            Console.Error.WriteLine("The callback did not contain an authorization code.");
            PrintRedirectHint(redirectUri);
            return 1;
        }
        code = callback.Code;

        try
        {
            using var http = new HttpClient { BaseAddress = new Uri("https://api.x.com/2/") };
            var token = await new XOAuthClient(http, clientId)
                .ExchangeAuthorizationCodeAsync(code, verifier, redirectUri, ct);

            var api = new XApiClient(http, new StaticAuthTokenProvider(token.AccessToken));
            var me = await api.GetMeAsync(ct);

            var now = SystemClock.Instance.UtcNow;
            new AuthFileStore().Save(new AuthSession
            {
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken,
                ExpiresAt = now.AddSeconds(token.ExpiresIn),
                Scope = token.Scope,
                UserId = me.Id,
                UserName = me.UserName,
            });

            Console.WriteLine($"Logged in as @{me.UserName}. Credentials stored in {AuthFilePath()}.");
            return 0;
        }
        catch (XApiException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintRedirectHint(redirectUri);
            return 1;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Could not reach the X token endpoint: {ex.Message}");
            PrintRedirectHint(redirectUri);
            return 1;
        }
    }

    static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            Console.WriteLine("Could not open a browser automatically; paste the URL above into one.");
        }
    }

    static void PrintRedirectHint(string redirectUri) =>
        Console.Error.WriteLine(
            $"If X rejected the redirect, register this exact URL as a User authentication redirect URI "
            + $"in your app on the X developer portal: {redirectUri}");

    static string AuthFilePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".worky", "auth.json");
}
