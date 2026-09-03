using System.Net;

namespace Omnichannel.Api.Endpoints;

internal static class ResetPasswordFormPage
{
    public static string Render(Guid userId, string token) => $"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>Reset password — Omnichannel</title>
        </head>
        <body style="margin:0; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif; background:#f4f4f5; display:flex; min-height:100vh; align-items:center; justify-content:center;">
          <form method="post" action="/api/v1/auth/password-reset/form" style="max-width:360px; width:100%; padding:32px; background:#fff; border:1px solid #e4e4e7; border-radius:12px;">
            <h1 style="margin:0 0 20px; font-size:18px; color:#18181b;">Choose a new password</h1>
            <input type="hidden" name="userId" value="{WebUtility.HtmlEncode(userId.ToString())}" />
            <input type="hidden" name="token" value="{WebUtility.HtmlEncode(token)}" />
            <label style="display:block; font-size:13px; color:#3f3f46; margin-bottom:6px;">New password</label>
            <input type="password" name="newPassword" required minlength="10" style="width:100%; box-sizing:border-box; padding:10px; border:1px solid #d4d4d8; border-radius:6px; margin-bottom:16px;" />
            <label style="display:block; font-size:13px; color:#3f3f46; margin-bottom:6px;">Confirm password</label>
            <input type="password" name="confirmPassword" required minlength="10" style="width:100%; box-sizing:border-box; padding:10px; border:1px solid #d4d4d8; border-radius:6px; margin-bottom:20px;" />
            <button type="submit" style="width:100%; padding:12px; background:#18181b; color:#fff; border:none; border-radius:8px; font-size:14px; font-weight:600; cursor:pointer;">Reset password</button>
          </form>
        </body>
        </html>
        """;
}
