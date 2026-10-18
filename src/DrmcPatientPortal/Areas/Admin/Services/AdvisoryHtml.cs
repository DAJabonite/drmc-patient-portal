using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;

namespace DrmcPatientPortal.Areas.Admin.Services;

public static class AdvisoryHtml
{
    private static readonly HashSet<string> RemovedSubtrees = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "template", "iframe", "object", "embed", "noscript", "textarea",
        "title", "xmp", "plaintext", "noembed", "noframes"
    };

    public static bool SafeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (value != value.Trim() || value.Any(char.IsControl) || value.Contains('\\') || value.StartsWith("//", StringComparison.Ordinal)) return false;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)) return absolute.Scheme == "https" && !string.IsNullOrEmpty(absolute.Host);
        return !value.Contains(':') && Uri.TryCreate(value, UriKind.Relative, out _);
    }

    public static string Clean(string html)
    {
        using var document = new HtmlParser(new HtmlParserOptions { IsScripting = true }).ParseDocument(html);
        var body = document.Body;
        if (body is null) return "";

        // Drop active/foreign subtrees before preserving children of ordinary unsupported formatting.
        foreach (var element in body.QuerySelectorAll("*"))
        {
            if (element.NamespaceUri != "http://www.w3.org/1999/xhtml" || RemovedSubtrees.Contains(element.LocalName))
            {
                element.Remove();
                continue;
            }

            foreach (var attribute in element.Attributes.ToArray())
                if (element.LocalName != "a" || attribute.Name != "href" || !string.IsNullOrEmpty(attribute.NamespaceUri))
                    element.RemoveAttribute(attribute.Name);

            // Inspect entity-decoded values before the library can normalize whitespace in URLs.
            if (element.LocalName == "a" && !SafeLink(element.GetAttribute("href")))
                element.RemoveAttribute("href");
        }

        var sanitizer = new HtmlSanitizer { KeepChildNodes = true, AllowDataAttributes = false };
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(["p", "ul", "ol", "li", "strong", "em", "b", "i", "h2", "h3", "h4", "blockquote", "br", "a"]);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.UriAttributes.Clear();
        sanitizer.UriAttributes.Add("href");
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["https", "http", "mailto"]);
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedAtRules.Clear();
        sanitizer.SanitizeDom(document, body);
        foreach (var comment in body.GetNodes<IComment>(true).ToArray()) comment.Remove();

        foreach (var link in body.QuerySelectorAll("a[href]"))
        {
            var href = link.GetAttribute("href");
            if (!SafeLink(href)) link.RemoveAttribute("href");
            else if (!href!.StartsWith('/')) link.SetAttribute("rel", "noopener noreferrer");
        }

        using var output = new StringWriter();
        foreach (var node in body.ChildNodes) node.ToHtml(output, sanitizer.OutputFormatter);
        return output.ToString();
    }

    private static bool SafeLink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsControl) || value.Contains('\\'))
            return false;
        if (value.StartsWith('/'))
            return !value.StartsWith("//", StringComparison.Ordinal) && Uri.TryCreate(value, UriKind.Relative, out _);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var absolute)) return false;
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return absolute.Scheme is "https" or "http" && !string.IsNullOrEmpty(absolute.Host);
        return absolute.Scheme == "mailto" && !string.IsNullOrWhiteSpace(value["mailto:".Length..].Split('?', '#')[0]);
    }
}
