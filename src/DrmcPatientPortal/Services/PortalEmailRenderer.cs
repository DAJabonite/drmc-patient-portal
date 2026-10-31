using System.Text;
using System.Text.Encodings.Web;

namespace DrmcPatientPortal.Services;

public sealed record PortalEmailDetail(string Text, bool IsCode = false);

public sealed record PortalEmailContent(
    string Subject,
    string Heading,
    string Preview,
    string Introduction,
    string ActionText,
    string ActionUrl,
    string Instructions,
    string UnexpectedGuidance,
    IReadOnlyList<PortalEmailDetail>? Details = null,
    string Language = "en",
    string FallbackLabel = "If the button does not work, open this link:");

public sealed record RenderedPortalEmail(string Subject, string HtmlBody, string PlainTextBody);

// Content is text, never caller-supplied markup. Render in the request's culture before queueing.
public static class PortalEmailRenderer
{
    public const string SealContentId = "drmc-seal";

    public static RenderedPortalEmail Render(PortalEmailContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!Uri.TryCreate(content.ActionUrl, UriKind.Absolute, out var action) ||
            action.Scheme is not ("https" or "http") || action.UserInfo.Length != 0)
            throw new ArgumentException("A portal email action must be an absolute HTTP or HTTPS URL.", nameof(content));

        static string Encode(string value) => HtmlEncoder.Default.Encode(value);
        var details = new StringBuilder();
        foreach (var detail in content.Details ?? [])
        {
            var typography = detail.IsCode
                ? "font-family:Consolas,'Courier New',monospace;font-size:17px;font-weight:bold;"
                : "font-size:15px;";
            details.Append($"<p style=\"margin:0 0 12px;line-height:1.6;overflow-wrap:anywhere;word-break:break-word;{typography}\">{Encode(detail.Text)}</p>");
        }
        var detailBlock = details.Length == 0 ? string.Empty : $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 24px;table-layout:fixed;">
              <tr><td style="padding:0;color:#17212b;">{details}</td></tr>
            </table>
            """;
        var url = Encode(content.ActionUrl);
        var html = $"""
            <!DOCTYPE html>
            <html lang="{Encode(content.Language)}">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{Encode(content.Heading)}</title></head>
            <body style="margin:0;padding:0;background-color:#f5f7fa;color:#17212b;font-family:Arial,'Helvetica Neue',sans-serif;">
              <div style="display:none;font-size:1px;line-height:1px;color:#f5f7fa;max-height:0;max-width:0;opacity:0;overflow:hidden;mso-hide:all;">{Encode(content.Preview)}</div>
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#f5f7fa">
                <tr><td align="center" style="padding:24px 8px;">
                  <!--[if mso]><table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0"><tr><td><![endif]-->
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#ffffff" style="width:100%;max-width:600px;table-layout:fixed;">
                    <tr><td align="center" style="padding:24px 20px 8px;">
                      <table role="presentation" align="center" cellpadding="0" cellspacing="0" border="0" style="max-width:100%;margin:0 auto;">
                        <tr><td width="64" valign="middle"><img src="cid:{SealContentId}" width="48" height="48" alt="DRMC seal" style="display:block;width:48px;height:48px;border:0;"></td>
                        <td valign="middle" align="left" style="color:#093963;font-size:20px;line-height:1.35;font-weight:bold;">DRMC Patient Portal</td></tr>
                      </table>
                    </td></tr>
                    <tr><td align="left" style="padding:28px 20px 24px;font-size:16px;line-height:1.65;color:#17212b;overflow-wrap:anywhere;word-break:break-word;">
                      <h1 style="margin:0 0 16px;font-size:24px;line-height:1.3;color:#17212b;">{Encode(content.Heading)}</h1>
                      <p style="margin:0 0 24px;">{Encode(content.Introduction)}</p>
                      {detailBlock}
                      <table role="presentation" align="center" cellpadding="0" cellspacing="0" border="0" style="max-width:100%;margin:0 auto 24px;">
                        <tr><td align="center" style="border-radius:6px;background-color:#0e4e87;"><a href="{url}" style="display:inline-block;padding:14px 20px;border:1px solid #0e4e87;border-radius:6px;color:#ffffff;font-size:16px;line-height:1.5;font-weight:bold;text-decoration:none;">{Encode(content.ActionText)}</a></td></tr>
                      </table>
                      <p style="margin:0 0 8px;font-size:13px;color:#536273;">{Encode(content.FallbackLabel)}</p>
                      <p style="margin:0 0 24px;font-size:13px;line-height:1.6;"><a href="{url}" style="color:#0e4e87;text-decoration:underline;">{Encode(content.ActionText)}</a></p>
                      <p style="margin:0;">{Encode(content.Instructions)}</p>
                    </td></tr>
                    <tr><td align="left" style="padding:20px;font-size:13px;line-height:1.6;color:#536273;border-top:1px solid #e5e9ee;">
                      <p style="margin:0 0 12px;">{Encode(content.UnexpectedGuidance)}</p>
                      <p style="margin:0;font-weight:bold;color:#093963;">DRMC Patient Portal</p>
                      <p style="margin:0;">Davao Regional Medical Center</p>
                    </td></tr>
                  </table>
                  <!--[if mso]></td></tr></table><![endif]-->
                </td></tr>
              </table>
            </body>
            </html>
            """;

        var text = new StringBuilder("DRMC Patient Portal\n\n");
        text.Append(content.Heading).Append("\n\n").Append(content.Introduction).Append("\n\n");
        foreach (var detail in content.Details ?? []) text.Append(detail.Text).Append("\n\n");
        text.Append(content.ActionText).Append(":\n").Append(content.ActionUrl).Append("\n\n")
            .Append(content.Instructions).Append("\n\n").Append(content.UnexpectedGuidance)
            .Append("\n\nDRMC Patient Portal\nDavao Regional Medical Center");
        return new(content.Subject, html, text.ToString());
    }
}
