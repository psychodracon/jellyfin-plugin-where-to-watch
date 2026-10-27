namespace Jellyfin.Plugin.WhereToWatch.Middleware;

/// <summary>
/// Adds this plugin's script to Jellyfin's web page.
/// </summary>
public static class HtmlInjector
{
    /// <summary>
    /// Inserts a script tag just before the final closing body tag.
    /// </summary>
    /// <param name="html">The page.</param>
    /// <param name="scriptUrl">URL of the script, as it should appear in the tag.</param>
    /// <returns>The page with the script added; unchanged when the script is already there or the page has no body.</returns>
    public static string Inject(string html, string scriptUrl)
    {
        if (html.Contains(scriptUrl, StringComparison.Ordinal))
        {
            return html;
        }

        // The last closing tag is the real one; an earlier "</body>" can only be text inside inline script.
        var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return end < 0 ? html : html.Insert(end, $"<script src=\"{scriptUrl}\" defer></script>");
    }
}
