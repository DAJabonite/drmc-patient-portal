using System.Net;
using System.Text.RegularExpressions;

namespace DrmcPatientPortal.Tests;

internal static partial class PortalClient
{
    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    public static async Task<HttpResponseMessage> SignInAsync(this HttpClient client, string email, string password)
    {
        var page = await client.GetStringAsync("/Identity/Account/Login");
        var token = AntiforgeryToken().Match(page);
        Assert.True(token.Success, "Login page did not render an antiforgery token.");
        return await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
        }));
    }

    public static async Task SignInSuccessfullyAsync(this HttpClient client, string email, string password)
    {
        var response = await client.SignInAsync(email, password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Patient/Home", response.Headers.Location?.OriginalString);
    }

    public static IReadOnlyList<string> Links(string html, string prefix) =>
        Regex.Matches(html, "href=\"(" + Regex.Escape(prefix) + "\\d+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
}
