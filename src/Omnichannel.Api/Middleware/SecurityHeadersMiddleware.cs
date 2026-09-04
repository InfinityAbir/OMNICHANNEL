namespace Omnichannel.Api.Middleware;

/// <summary>
/// Adds baseline security headers to every response. The API surface itself (/api, /hubs) is pure
/// JSON/WebSocket, not a document renderer, so its CSP stays locked down to "block everything" —
/// but the same process also serves the built Angular SPA (Phase 16/ADR-0028: one Render web
/// service, one origin) and the public widget embed, both of which need to load their own
/// same-origin script/style/image assets, so each gets its own, narrower relaxation.
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
            headers["Content-Security-Policy"] = BuildContentSecurityPolicy(context.Request.Path);
            headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
            headers.Remove("Server");
            headers.Remove("X-Powered-By");
            return Task.CompletedTask;
        });

        await next(context);
    }

    private static string BuildContentSecurityPolicy(PathString path)
    {
        // The public self-hosted widget + demo page: contains no tenant data (all logic lives in
        // the /widget API), so same-origin 'self' is both necessary and safe.
        if (path.StartsWithSegments("/widget"))
        {
            return "default-src 'self'";
        }

        // The API surface itself never renders anything — no script/style/image needs to load.
        if (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs") || path.StartsWithSegments("/health"))
        {
            return "default-src 'none'; frame-ancestors 'none'";
        }

        // Everything else is the Angular SPA (ADR-0028). 'unsafe-inline' is scoped to style-src
        // only (Angular's runtime injects component styles as inline <style> tags) — script-src
        // stays 'self'-only, since inline *script* injection is the primary XSS threat CSP
        // defends against, not inline styles.
        return "default-src 'self'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'";
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
