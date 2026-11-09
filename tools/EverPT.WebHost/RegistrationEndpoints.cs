using System.Data;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using PristonTale.Persistence.Abstractions.Security;

internal static class RegistrationEndpoints
{
    private sealed record RegistrationRequest(string? Username, string? Password, string? ConfirmPassword, string? Website);

    public static void MapRegistration(this WebApplication app, string siteRoot)
    {
        app.MapGet("/cadastro", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            var path = Path.Combine(siteRoot, "cadastro.html");
            return File.Exists(path) ? Results.File(path, "text/html; charset=utf-8") : Results.NotFound();
        });

        app.MapPost("/api/register", async (HttpContext context, ILoggerFactory logFactory) =>
        {
            // HTTPS is terminated by the Cloudflare Tunnel. Verify the actual
            // same-origin browser request: no credentials in URL or access logs.
            if (!string.Equals(context.Request.ContentType?.Split(';')[0], "application/json", StringComparison.OrdinalIgnoreCase))
                return Results.StatusCode(415);
            var originText = context.Request.Headers.Origin.ToString();
            if (!Uri.TryCreate(originText, UriKind.Absolute, out var origin)
                || origin.Scheme != Uri.UriSchemeHttps
                || !string.Equals(origin.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase))
                return Results.StatusCode(403);

            if (context.Request.ContentLength is > 4096)
                return Results.StatusCode(413);

            RegistrationRequest? input;
            try
            {
                input = await context.Request.ReadFromJsonAsync<RegistrationRequest>(cancellationToken: context.RequestAborted);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "invalid_request" });
            }
            if (input is null || !string.IsNullOrWhiteSpace(input.Website))
                return Results.BadRequest(new { error = "invalid_request" });

            var username = input.Username?.Trim() ?? "";
            var password = input.Password ?? "";
            if (!Regex.IsMatch(username, @"\A[a-zA-Z0-9_]{3,20}\z", RegexOptions.CultureInvariant))
                return Results.BadRequest(new { error = "invalid_username" });
            // The current native login panel accepts at most 20 characters.
            if (password.Length is < 8 or > 20 || password.Any(char.IsControl))
                return Results.BadRequest(new { error = "invalid_password" });
            if (password != input.ConfirmPassword)
                return Results.BadRequest(new { error = "password_mismatch" });

            var database = Environment.GetEnvironmentVariable("EVERPT_ACCOUNT_DB");
            if (string.IsNullOrWhiteSpace(database) || !File.Exists(database))
                return Results.Json(new { error = "registration_unavailable" }, statusCode: 503);

            try
            {
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = database,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = true,
                    DefaultTimeout = 5
                };
                await using var connection = new SqliteConnection(builder.ToString());
                await connection.OpenAsync(context.RequestAborted);

                await using (var existing = connection.CreateCommand())
                {
                    existing.CommandText = "SELECT 1 FROM accounts WHERE user_id = $user COLLATE NOCASE LIMIT 1";
                    existing.Parameters.AddWithValue("$user", username);
                    if (await existing.ExecuteScalarAsync(context.RequestAborted) is not null)
                        return Results.Conflict(new { error = "username_taken" });
                }

                // The same PBKDF2-SHA256 with random salt used by AuthAppService.
                var hashed = PasswordHasher.Hash(password);
                await using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO accounts (user_id, password_hash) VALUES ($user, $hash)";
                insert.Parameters.AddWithValue("$user", username);
                insert.Parameters.AddWithValue("$hash", hashed);
                await insert.ExecuteNonQueryAsync(context.RequestAborted);
                return Results.Json(new { ok = true }, statusCode: StatusCodes.Status201Created);
            }
            catch (SqliteException error) when (error.SqliteErrorCode == 19)
            {
                return Results.Conflict(new { error = "username_taken" });
            }
            catch (Exception error) when (error is SqliteException or IOException)
            {
                logFactory.CreateLogger("EverPT.Registration").LogWarning(
                    "Account registration unavailable: {ErrorType}", error.GetType().Name);
                return Results.Json(new { error = "registration_unavailable" }, statusCode: 503);
            }
        })
        .RequireRateLimiting("everpt-registration")
        .WithMetadata(new RequestSizeLimitAttribute(4096));
    }

    public static string RegistrationRateKey(HttpContext context)
    {
        // The web server is bound to loopback and reached through Cloudflare
        // Tunnel; CF-Connecting-IP is therefore set by the trusted tunnel.
        var forwarded = context.Request.Headers["CF-Connecting-IP"].ToString();
        return IPAddress.TryParse(forwarded, out var parsed)
            ? parsed.ToString()
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
