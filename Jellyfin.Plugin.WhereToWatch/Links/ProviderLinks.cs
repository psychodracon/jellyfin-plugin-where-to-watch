namespace Jellyfin.Plugin.WhereToWatch.Links;

/// <summary>
/// Search pages of streaming providers, for linking a logo to the provider's own results for a title.
/// Only providers with a public search URL are listed; others (Disney+ and HBO Max have none) fall back to the TMDB watch page.
/// </summary>
public static class ProviderLinks
{
    // Keyed by TMDB provider id. The value is the URL up to and including the search parameter.
    private static readonly Dictionary<int, string> SearchPages = new()
    {
        [8] = "https://www.netflix.com/search?q=",
        [1796] = "https://www.netflix.com/search?q=",
        [119] = "https://www.primevideo.com/search/ref=atv_nb_sr?phrase=",
        [2100] = "https://www.primevideo.com/search/ref=atv_nb_sr?phrase=",
    };

    /// <summary>
    /// Builds the provider's search page for a title.
    /// </summary>
    /// <param name="providerId">TMDB provider id.</param>
    /// <param name="title">The title to search for.</param>
    /// <returns>The URL, or null when the provider has no known search page or the title is blank.</returns>
    public static string? SearchUrl(int providerId, string title)
    {
        if (string.IsNullOrWhiteSpace(title) || !SearchPages.TryGetValue(providerId, out var prefix))
        {
            return null;
        }

        return prefix + Uri.EscapeDataString(title.Trim());
    }
}
