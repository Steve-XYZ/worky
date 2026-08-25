using System.Net.Sockets;
using System.Text;

namespace Worky.Core.Auth;

public sealed record LoginCallbackResult(string? Code, string? Error);

public static class LoginCallback
{
    public const string TimeoutError = "timeout";
    public const string TooManyConnectionsError = "too_many_connections";

    static readonly TimeSpan DefaultDeadline = TimeSpan.FromMinutes(5);
    static readonly TimeSpan DefaultIdleReadTimeout = TimeSpan.FromSeconds(10);
    const int DefaultMaxConnections = 50;

    public static Task<LoginCallbackResult> AwaitAsync(
        TcpListener listener, string expectedState, CancellationToken ct = default)
        => AwaitAsync(listener, expectedState, DefaultDeadline, DefaultMaxConnections, DefaultIdleReadTimeout, ct);

    public static async Task<LoginCallbackResult> AwaitAsync(
        TcpListener listener,
        string expectedState,
        TimeSpan deadline,
        int maxConnections,
        TimeSpan idleReadTimeout,
        CancellationToken ct = default)
    {
        using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadlineCts.CancelAfter(deadline);

        for (var handled = 0; handled < maxConnections; handled++)
        {
            TcpClient tcp;
            try
            {
                tcp = await listener.AcceptTcpClientAsync(deadlineCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new LoginCallbackResult(null, TimeoutError);
            }

            using (tcp)
            {
                LoginCallbackResult? result;
                try
                {
                    result = await ReadCallbackAsync(tcp.GetStream(), expectedState, idleReadTimeout, deadlineCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return new LoginCallbackResult(null, TimeoutError);
                }

                if (result is not null) return result;
            }
        }

        return new LoginCallbackResult(null, TooManyConnectionsError);
    }

    static async Task<LoginCallbackResult?> ReadCallbackAsync(
        NetworkStream stream, string expectedState, TimeSpan idleReadTimeout, CancellationToken ct)
    {
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idleCts.CancelAfter(idleReadTimeout);

        string requestLine;
        try
        {
            requestLine = await ReadRequestLineAsync(stream, idleCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }

        var stateMatches = GetQueryValue(requestLine, "state") == expectedState;
        var code = GetQueryValue(requestLine, "code");
        var error = GetQueryValue(requestLine, "error_description") ?? GetQueryValue(requestLine, "error");

        if (!stateMatches || (code is null && error is null))
        {
            var hasCallbackParams =
                code is not null || error is not null || GetQueryValue(requestLine, "state") is not null;
            if (hasCallbackParams) await WriteCallbackPageAsync(stream, success: false, ct);
            return null;
        }

        await WriteCallbackPageAsync(stream, success: error is null, ct);
        return new LoginCallbackResult(code, error);
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
}
