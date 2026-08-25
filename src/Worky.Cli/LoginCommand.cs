using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
        var callback = await AwaitCallbackAsync(listener, state, redirectUri, ct);
        if (callback.Error is not null)
        {
            Console.Error.WriteLine($"X reported an authorization error: {callback.Error}");
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
            new AuthFileStore().Save(new AuthSession(
                token.AccessToken,
                token.RefreshToken,
                now.AddSeconds(token.ExpiresIn),
                token.Scope,
                me.Id,
                me.UserName));

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

    static async Task<(string? Code, string? Error)> AwaitCallbackAsync(
        TcpListener listener, string expectedState, string redirectUri, CancellationToken ct)
    {
        using var tcp = await listener.AcceptTcpClientAsync(ct);
        await using var stream = tcp.GetStream();

        var requestLine = await ReadRequestLineAsync(stream, ct);
        var stateMatches = GetQueryValue(requestLine, "state") == expectedState;
        var error = GetQueryValue(requestLine, "error_description") ?? GetQueryValue(requestLine, "error");
        var code = GetQueryValue(requestLine, "code");

        await WriteCallbackPageAsync(stream, success: error is null && code is not null && stateMatches, ct);

        if (!stateMatches)
        {
            Console.Error.WriteLine("Callback state did not match the authorization request; rejecting it.");
            return (null, "state_mismatch");
        }
        return (code, error);
    }

    static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[1024];
        var received = new StringBuilder();
        while (received.Length < 8192)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            received.Append(Encoding.ASCII.GetString(buffer, 0, read));
            var line = received.ToString();
            var end = line.IndexOf("\r\n", StringComparison.Ordinal);
            if (end >= 0) return line[..end];
        }
        return received.ToString();
    }

    static async Task WriteCallbackPageAsync(NetworkStream stream, bool success, CancellationToken ct)
    {
        var body = success
            ? "<html><body><h2>Login complete</h2><p>You can close this window.</p></body></html>"
            : "<html><body><h2>Login failed</h2><p>Check the terminal running worky.</p></body></html>";
        var head =
            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nConnection: close\r\n"
            + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(head + body), ct);
    }

    static string? GetQueryValue(string requestLine, string name)
    {
        var parts = requestLine.Split(' ', 3);
        if (parts.Length < 2) return null;
        var target = parts[1];
        var queryStart = target.IndexOf('?');
        if (queryStart < 0) return null;

        foreach (var pair in target[(queryStart + 1)..].Split('&'))
        {
            var eq = pair.IndexOf('=');
            var key = eq < 0 ? pair : pair[..eq];
            if (key != name) continue;
            var raw = eq < 0 ? "" : pair[(eq + 1)..];
            return Uri.UnescapeDataString(raw.Replace('+', ' '));
        }
        return null;
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
