namespace Jellyfin.Plugin.WhereToWatch.Middleware;

/// <summary>
/// Adds this plugin's script to Jellyfin's web page.
/// </summary>
public static class HtmlInjector
{
    private const string Marker = "data-wheretowatch";

    /// <summary>
    /// Inserts the script inline, just before the final closing body tag.
    /// </summary>
    /// <param name="html">The page.</param>
    /// <param name="scriptSource">JavaScript source to run on the page.</param>
    /// <returns>The page with the script added; unchanged when the script is already there or the page has no body.</returns>
    public static string Inject(string html, string scriptSource)
    {
        if (html.Contains(Marker, StringComparison.Ordinal))
        {
            return html;
        }

        // The last closing tag is the real one; an earlier "</body>" can only be text inside inline script.
        var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return end < 0 ? html : html.Insert(end, $"<script {Marker}>{EscapeClosingTag(scriptSource)}</script>");
    }

    // A literal closing script tag inside the source would end the element early; "<\/" is the same text to JavaScript.
    private static string EscapeClosingTag(string source) =>
        source.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
}
