using AngleSharp.Html.Parser;
using Ganss.Xss;

namespace DrmcPatientPortal.Areas.Admin.Services;

public static class AdvisoryHtml
{
    public static bool SafeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (value != value.Trim() || value.Any(char.IsControl) || value.Contains('\\') || value.StartsWith("//", StringComparison.Ordinal)) return false;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)) return absolute.Scheme == "https" && !string.IsNullOrEmpty(absolute.Host);
        return !value.Contains(':') && Uri.TryCreate(value, UriKind.Relative, out _);
    }

    public static string Clean(string html)
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(["p", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "strong", "em", "b", "i", "br", "table", "thead", "tbody", "tfoot", "tr", "th", "td", "a"]);
        sanitizer.AllowedAttributes.Clear(); sanitizer.AllowedAttributes.UnionWith(["href", "title", "colspan", "rowspan"]);
        sanitizer.AllowedSchemes.Clear(); sanitizer.AllowedSchemes.Add("https"); sanitizer.AllowedCssProperties.Clear();
        var document = new HtmlParser().ParseDocument(sanitizer.Sanitize(html));
        foreach (var link in document.QuerySelectorAll("a[href]"))
            if (!SafeUrl(link.GetAttribute("href"))) link.RemoveAttribute("href");
        return document.Body?.InnerHtml ?? "";
    }
}
