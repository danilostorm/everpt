using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("EVERPT_WEB_BIND") ?? "http://127.0.0.1:8088");
var app = builder.Build();

var siteRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("EVERPT_WEB_ROOT")
    ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"));
if (!Directory.Exists(siteRoot))
    throw new DirectoryNotFoundException($"EverPT Web site directory not found: {siteRoot}");

var pin = Environment.GetEnvironmentVariable("EVERPT_SERVER_CERT_PIN");
if (string.IsNullOrWhiteSpace(pin) || pin.Length != 64 || !pin.All(Uri.IsHexDigit))
    throw new InvalidOperationException("EVERPT_SERVER_CERT_PIN must be the 64-character SHA-256 of the game server certificate.");

app.Use(async (context, next) =>
{
    // WebAssembly threads and SharedArrayBuffer require cross-origin isolation.
    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
    context.Response.Headers["Cross-Origin-Embedder-Policy"] = "require-corp";
    context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

app.MapGet("/healthz", () => Results.Json(new
{
    app = "EverPT Web",
    webAssemblyReady = Directory.Exists(Path.Combine(siteRoot, "_framework"))
}));

app.Map("/ws.ashx", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    if (context.Request.Query["port"].ToString() != "10009"
        || !Uri.TryCreate(context.Request.Headers.Origin.ToString(), UriKind.Absolute, out var origin)
        || !string.Equals(origin.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
        || origin.Scheme != "https")
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    using var tcp = new TcpClient { NoDelay = true };
    try
    {
        await tcp.ConnectAsync(IPAddress.Loopback, 10009, context.RequestAborted);
        using var tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
            (_, certificate, _, _) => certificate is not null
                && string.Equals(Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())), pin,
                    StringComparison.OrdinalIgnoreCase));
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "localhost",
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, context.RequestAborted);

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var toServer = ForwardWebSocketToTls(socket, tls, cancel.Token);
        var toBrowser = ForwardTlsToWebSocket(tls, socket, cancel.Token);
        await Task.WhenAny(toServer, toBrowser);
        await cancel.CancelAsync();
        try { await Task.WhenAll(toServer, toBrowser); } catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (IOException) { }
        if (socket.State == WebSocketState.Open)
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
    }
    catch (Exception ex) when (ex is SocketException or AuthenticationException or IOException or OperationCanceledException)
    {
        app.Logger.LogWarning("EverPT WebSocket game bridge disconnected: {Type}", ex.GetType().Name);
        if (socket.State == WebSocketState.Open)
            await socket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "Server unavailable", CancellationToken.None);
    }
});

app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new PhysicalFileProvider(siteRoot) });
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(siteRoot) });

// Game assets are served only if explicitly enabled. Public distribution requires rights to those assets.
var assetsRoot = Environment.GetEnvironmentVariable("EVERPT_WEB_ASSETS_ROOT");
if (!string.IsNullOrWhiteSpace(assetsRoot) && Directory.Exists(assetsRoot))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.GetFullPath(assetsRoot)),
        RequestPath = "/assets",
        ServeUnknownFileTypes = true,
        DefaultContentType = "application/octet-stream"
    });
}

await app.RunAsync();

static async Task ForwardWebSocketToTls(WebSocket ws, SslStream tls, CancellationToken ct)
{
    var bytes = new byte[65536];
    while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
    {
        var result = await ws.ReceiveAsync(bytes, ct);
        if (result.MessageType == WebSocketMessageType.Close) return;
        if (result.MessageType != WebSocketMessageType.Binary) return;
        if (result.Count > 0) await tls.WriteAsync(bytes.AsMemory(0, result.Count), ct);
    }
}

static async Task ForwardTlsToWebSocket(SslStream tls, WebSocket ws, CancellationToken ct)
{
    var bytes = new byte[65536];
    while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
    {
        var n = await tls.ReadAsync(bytes, ct);
        if (n == 0) return;
        await ws.SendAsync(bytes.AsMemory(0, n), WebSocketMessageType.Binary, true, ct);
    }
}
