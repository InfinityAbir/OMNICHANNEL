using System.Net;

namespace Omnichannel.Api.Endpoints;

/// <summary>
/// Minimal server-rendered outcome page for link-follow flows (email confirm / password reset)
/// that have no Angular route to land on yet — Phase 3 replaces this with a real frontend page.
/// Deliberately plain; the transactional emails are the piece the user asked to be well-designed.
/// </summary>
internal static class ResultPage
{
    public static string Render(string title, string message, bool success) => $"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>{WebUtility.HtmlEncode(title)} — Omnichannel</title>
        </head>
        <body style="margin:0; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif; background:#f4f4f5; display:flex; min-height:100vh; align-items:center; justify-content:center;">
          <div style="max-width:420px; padding:32px; background:#fff; border:1px solid #e4e4e7; border-radius:12px; text-align:center;">
            <h1 style="margin:0 0 12px; font-size:20px; color:{(success ? "#18181b" : "#b91c1c")};">{WebUtility.HtmlEncode(title)}</h1>
            <p style="margin:0; font-size:14px; color:#3f3f46; line-height:1.6;">{WebUtility.HtmlEncode(message)}</p>
          </div>
        </body>
        </html>
        """;
}
