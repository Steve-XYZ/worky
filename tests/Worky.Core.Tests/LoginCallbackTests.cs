using System.Net;
using System.Net.Sockets;
using System.Text;
using Worky.Core.Auth;

namespace Worky.Core.Tests;

public class LoginCallbackTests : IDisposable
{
    static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(5);

    readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    readonly List<TcpClient> _clients = [];

    public LoginCallbackTests() => _listener.Start();

    public void Dispose()
    {
        foreach (var client in _clients) client.Dispose();
        _listener.Stop();
    }

    int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    [Fact]
    public async Task AcceptsValidCallbackAfterSkippingGarbageConnection()
    {
        var stray = await ConnectAsync();
        await SendAsync(stray, "NOT-HTTP\r\n\r\n");

        var wait = LoginCallback.AwaitAsync(_listener, "st-22", Deadline, 5, IdleTimeout);
        var good = await ConnectAsync();
        await SendAsync(good, "GET /callback?code=ac-1&state=st-22 HTTP/1.1\r\nHost: x\r\n\r\n");

        var result = await wait;

        Assert.Equal("ac-1", result.Code);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task SkipsSilentConnectionHoldingThePort()
    {
        var silent = await ConnectAsync();

        var wait = LoginCallback.AwaitAsync(
            _listener, "st", Deadline, 5, TimeSpan.FromMilliseconds(300));
        var good = await ConnectAsync();
        await SendAsync(good, "GET /callback?code=ok&state=st HTTP/1.1\r\n\r\n");

        var result = await wait;

        Assert.Equal("ok", result.Code);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task RejectsMismatchedStateThenAcceptsRealCallback()
    {
        var evil = await ConnectAsync();
        await SendAsync(evil, "GET /callback?code=evil&state=other HTTP/1.1\r\n\r\n");

        var wait = LoginCallback.AwaitAsync(_listener, "mine", Deadline, 5, IdleTimeout);
        var good = await ConnectAsync();
        await SendAsync(good, "GET /callback?code=real&state=mine HTTP/1.1\r\n\r\n");
        var result = await wait;

        Assert.Equal("real", result.Code);
        var evilResponse = await ReadAllAsync(evil);
        Assert.StartsWith("HTTP/1.1 200 OK", evilResponse);
        Assert.Contains("Login failed", evilResponse);
    }

    [Fact]
    public async Task SurfacesAuthorizationErrorFromMatchingState()
    {
        var wait = LoginCallback.AwaitAsync(_listener, "st", Deadline, 5, IdleTimeout);
        var client = await ConnectAsync();
        await SendAsync(client, "GET /callback?error=access_denied&error_description=The%20user%20denied&state=st HTTP/1.1\r\n\r\n");

        var result = await wait;

        Assert.Null(result.Code);
        Assert.Equal("The user denied", result.Error);
    }

    [Fact]
    public async Task ReportsTimeoutWhenNoUsableCallbackArrives()
    {
        var result = await LoginCallback.AwaitAsync(
            _listener, "st", TimeSpan.FromMilliseconds(250), 5, IdleTimeout);

        Assert.Null(result.Code);
        Assert.Equal(LoginCallback.TimeoutError, result.Error);
    }

    [Fact]
    public async Task GivesUpAfterTooManyStrayConnections()
    {
        for (var i = 0; i < 2; i++)
        {
            var stray = await ConnectAsync();
            await SendAsync(stray, "junk\r\n\r\n");
        }

        var result = await LoginCallback.AwaitAsync(_listener, "st", Deadline, maxConnections: 2, idleReadTimeout: IdleTimeout);

        Assert.Null(result.Code);
        Assert.Equal(LoginCallback.TooManyConnectionsError, result.Error);
    }

    async Task<TcpClient> ConnectAsync()
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, Port);
        _clients.Add(client);
        return client;
    }

    static async Task SendAsync(TcpClient client, string request)
    {
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(request));
    }

    static async Task<string> ReadAllAsync(TcpClient client)
    {
        var stream = client.GetStream();
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
            ms.Write(buffer, 0, read);
        return Encoding.ASCII.GetString(ms.ToArray());
    }
}
