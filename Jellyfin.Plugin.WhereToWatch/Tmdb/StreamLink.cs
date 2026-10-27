namespace Jellyfin.Plugin.WhereToWatch.Tmdb;

/// <summary>
/// A subscription provider's link as listed on TMDB's watch page.
/// </summary>
/// <param name="ProviderId">The page's own provider id. It usually equals TMDB's API id, but not always (Disney+ in Poland differs).</param>
/// <param name="Name">The provider's name as shown on the page.</param>
/// <param name="Url">The link, on <c>click.justwatch.com</c>, exactly as TMDB's page gives it.</param>
public sealed record StreamLink(int ProviderId, string Name, string Url);

/// <summary>
/// Picks the link that belongs to a TMDB provider from a page's links.
/// </summary>
public static class StreamLinkMatcher
{
    /// <summary>
    /// Finds the link for a provider, by id first and by name when the page uses a different id for the same service.
    /// </summary>
    /// <param name="links">The links read from the page.</param>
    /// <param name="providerId">TMDB's provider id.</param>
    /// <param name="providerName">TMDB's provider name.</param>
    /// <returns>The link, or null when none matches.</returns>
    public static string? Find(IReadOnlyList<StreamLink> links, int providerId, string providerName)
    {
        var byId = links.FirstOrDefault(l => l.ProviderId == providerId);
        if (byId is not null)
        {
            return byId.Url;
        }

        var wanted = Normalize(providerName);
        return wanted.Length == 0 ? null : links.FirstOrDefault(l => Normalize(l.Name) == wanted)?.Url;
    }

    // "Disney+", "Disney Plus" and "DISNEY  PLUS" are the same service.
    private static string Normalize(string name)
    {
        var letters = name.Replace("+", "plus", StringComparison.Ordinal).Where(char.IsLetterOrDigit);
        return new string(letters.ToArray()).ToLowerInvariant();
    }
}
