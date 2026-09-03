namespace Omnichannel.Api.Middleware;

/// <summary>
/// Adds baseline security headers to every response. This is a JSON API, not a
/// document renderer, so the CSP is intentionally locked down to "block everything".
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            // The public self-hosted widget + demo page must be able to load their own same-origin
            // script/style assets. It contains no tenant data (all logic lives in the /widget API),
            // so 'self' is both necessary and safe here. Everything else stays locked down.
            headers["Content-Security-Policy"] = context.Request.Path.StartsWithSegments("/widget")
                ? "default-src 'self'"
                : "default-src 'none'; frame-ancestors 'none'";
            headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
            headers.Remove("Server");
            headers.Remove("X-Powered-By");
            return Task.CompletedTask;
        });

        await next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
