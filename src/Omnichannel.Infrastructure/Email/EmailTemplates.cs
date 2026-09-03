using System.Net;

namespace Omnichannel.Infrastructure.Email;

/// <summary>
/// Inline-styled HTML (email clients don't reliably support external/embedded stylesheets) —
/// single-accent, monochromatic layout consistent with the product's visual direction.
/// </summary>
internal static class EmailTemplates
{
    public static (string Subject, string Html, string PlainText) EmailConfirmation(string displayName, string confirmationLink)
    {
        const string subject = "Confirm your Omnichannel account";
        var name = WebUtility.HtmlEncode(displayName);
        var link = WebUtility.HtmlEncode(confirmationLink);

        var html = Layout(
            preheader: "Confirm your email to finish setting up Omnichannel.",
            heading: "Confirm your email",
            bodyHtml: $"""
                <p style="margin:0 0 16px;">Hi {name},</p>
                <p style="margin:0 0 24px;">
                    Thanks for creating an Omnichannel account. Confirm your email address to
                    activate it and start setting up your inbox.
                </p>
                """,
            buttonText: "Confirm email",
            buttonLink: link,
            footerNote: "This link expires soon and can only be used once. If you didn't create an Omnichannel account, you can safely ignore this email.");

        var plainText =
            $"Hi {displayName},\n\n" +
            "Confirm your Omnichannel account by opening this link:\n" +
            $"{confirmationLink}\n\n" +
            "If you didn't create an Omnichannel account, you can ignore this email.";

        return (subject, html, plainText);
    }

    public static (string Subject, string Html, string PlainText) PasswordReset(string displayName, string resetLink)
    {
        const string subject = "Reset your Omnichannel password";
        var name = WebUtility.HtmlEncode(displayName);
        var link = WebUtility.HtmlEncode(resetLink);

        var html = Layout(
            preheader: "Reset your Omnichannel password.",
            heading: "Reset your password",
            bodyHtml: $"""
                <p style="margin:0 0 16px;">Hi {name},</p>
                <p style="margin:0 0 24px;">
                    We received a request to reset your Omnichannel password. Choose a new
                    password using the button below.
                </p>
                """,
            buttonText: "Reset password",
            buttonLink: link,
            footerNote: "This link expires soon and can only be used once. If you didn't request a password reset, you can safely ignore this email — your password won't change.");

        var plainText =
            $"Hi {displayName},\n\n" +
            "Reset your Omnichannel password by opening this link:\n" +
            $"{resetLink}\n\n" +
            "If you didn't request this, you can ignore this email.";

        return (subject, html, plainText);
    }

    private static string Layout(string preheader, string heading, string bodyHtml, string buttonText, string buttonLink, string footerNote)
        => $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>Omnichannel</title>
            </head>
            <body style="margin:0; padding:0; background-color:#f4f4f5; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
              <span style="display:none; font-size:1px; color:#f4f4f5; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;">
                {WebUtility.HtmlEncode(preheader)}
              </span>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f4f5; padding:32px 16px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:480px; background-color:#ffffff; border-radius:12px; overflow:hidden; border:1px solid #e4e4e7;">
                      <tr>
                        <td style="padding:28px 32px; border-bottom:1px solid #e4e4e7;">
                          <span style="font-size:18px; font-weight:700; color:#18181b; letter-spacing:-0.02em;">Omnichannel</span>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:32px;">
                          <h1 style="margin:0 0 20px; font-size:20px; font-weight:700; color:#18181b;">{WebUtility.HtmlEncode(heading)}</h1>
                          <div style="font-size:15px; line-height:1.6; color:#3f3f46;">
                            {bodyHtml}
                          </div>
                          <table role="presentation" cellpadding="0" cellspacing="0" style="margin:8px 0 28px;">
                            <tr>
                              <td style="border-radius:8px; background-color:#18181b;">
                                <a href="{buttonLink}" style="display:inline-block; padding:12px 24px; font-size:14px; font-weight:600; color:#ffffff; text-decoration:none; border-radius:8px;">
                                  {WebUtility.HtmlEncode(buttonText)}
                                </a>
                              </td>
                            </tr>
                          </table>
                          <p style="margin:0 0 8px; font-size:13px; color:#71717a;">
                            Or paste this link into your browser:
                          </p>
                          <p style="margin:0; font-size:13px; color:#3f82f6; word-break:break-all;">
                            {buttonLink}
                          </p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:20px 32px; background-color:#fafafa; border-top:1px solid #e4e4e7;">
                          <p style="margin:0; font-size:12px; line-height:1.6; color:#a1a1aa;">
                            {WebUtility.HtmlEncode(footerNote)}
                          </p>
                        </td>
                      </tr>
                    </table>
                    <p style="margin:20px 0 0; font-size:12px; color:#a1a1aa;">Omnichannel — one inbox for every customer conversation.</p>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
}
