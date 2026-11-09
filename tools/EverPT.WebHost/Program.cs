using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;

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
// MonoGame's compiled shaders and fonts are intentionally custom binary files.
// StaticFileMiddleware otherwise returns 404 for these unknown extensions.
var types = new FileExtensionContentTypeProvider();
types.Mappings[".mgfxo"] = "application/octet-stream";
types.Mappings[".xnb"] = "application/octet-stream";
// Full .NET globalization may ship ICU data blobs with a .dat extension.
types.Mappings[".dat"] = "application/octet-stream";
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(siteRoot), ContentTypeProvider = types });

// Assets from the third-party game archive must be explicitly enabled, and only
// files listed in the public manifest may be served. The same archive also contains
// GameServer and save data, which must never become web-accessible.
var assetsRoot = Environment.GetEnvironmentVariable("EVERPT_WEB_ASSETS_ROOT");
if (!string.IsNullOrWhiteSpace(assetsRoot) && Directory.Exists(assetsRoot))
{
    var manifestPath = Path.Combine(siteRoot, "files.json");
    if (!File.Exists(manifestPath))
        throw new FileNotFoundException("Public assets require a generated files.json manifest", manifestPath);

    using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
    var publicFiles = new HashSet<string>(StringComparer.Ordinal);
    foreach (var tree in manifest.RootElement.GetProperty("trees").EnumerateArray())
    {
        if (tree[0].GetString() != "/assets")
            continue;
        foreach (var file in tree[2].EnumerateArray())
            if (file[0].GetString() is { } relative)
                publicFiles.Add(relative);
    }

    var physicalRoot = Path.GetFullPath(assetsRoot);
    app.MapGet("/assets/{**assetPath}", (string assetPath) =>
    {
        var path = assetPath.Replace('\\', '/');
        if (!publicFiles.Contains(path) ||
            path.Split('/').Any(part => part is "" or "." or ".."))
            return Results.NotFound();

        var fullPath = Path.GetFullPath(Path.Combine(physicalRoot, path));
        if (!fullPath.StartsWith(physicalRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !File.Exists(fullPath))
            return Results.NotFound();

        return Results.File(fullPath, "application/octet-stream", enableRangeProcessing: true);
    });
    app.Logger.LogInformation("EverPT public manifest allows {Count} game asset files", publicFiles.Count);
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
