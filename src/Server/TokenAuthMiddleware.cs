using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SunsetMP.Server;

/// <summary>
/// Пускает к /api только с правильным токеном. Токен можно передать:
///   • заголовком  Authorization: Bearer &lt;токен&gt;
///   • заголовком  X-Api-Key: &lt;токен&gt;
///   • параметром  ?token=&lt;токен&gt;  (для плеера и скачивания, где заголовки не добавить)
/// </summary>
public sealed class TokenAuthMiddleware
{
    readonly RequestDelegate _next;
    readonly byte[] _expected;

    public TokenAuthMiddleware(RequestDelegate next, IOptions<AuthOptions> options)
    {
        _next = next;
        var token = string.IsNullOrWhiteSpace(options.Value.Token) ? AuthOptions.DefaultToken : options.Value.Token;
        _expected = Encoding.UTF8.GetBytes(token);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var supplied = ExtractToken(context.Request);
        if (supplied != null &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), _expected))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers["WWW-Authenticate"] = "Bearer";
        await context.Response.WriteAsJsonAsync(new { error = "Неверный или отсутствующий токен" });
    }

    static string? ExtractToken(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();

        var key = request.Headers["X-Api-Key"].ToString();
        if (!string.IsNullOrEmpty(key)) return key;

        var query = request.Query["token"].ToString();
        return string.IsNullOrEmpty(query) ? null : query;
    }
}
