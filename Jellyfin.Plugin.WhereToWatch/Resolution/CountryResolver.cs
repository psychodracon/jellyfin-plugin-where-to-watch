namespace Jellyfin.Plugin.WhereToWatch.Resolution;

/// <summary>
/// A streaming provider as identified by TMDB.
/// </summary>
/// <param name="Id">TMDB provider id.</param>
/// <param name="Name">TMDB provider name.</param>
public sealed record ProviderRef(int Id, string Name);

/// <summary>
/// The outcome of resolving an item's providers.
/// </summary>
/// <param name="Country">The country the providers were found in, or null when none matched.</param>
/// <param name="Providers">The selected providers available in <paramref name="Country"/>, in priority order.</param>
public sealed record ProviderResolution(string? Country, IReadOnlyList<ProviderRef> Providers);

/// <summary>
/// Picks the country an item's providers are resolved from.
/// </summary>
public static class CountryResolver
{
    /// <summary>
    /// Resolves selected providers using an ordered country fallback.
    /// </summary>
    /// <param name="countries">Countries to try, primary first.</param>
    /// <param name="selectedProviderIds">Selected provider ids in priority order.</param>
    /// <param name="flatrateByCountry">Flatrate providers the item has, per country.</param>
    /// <returns>The first country with a selected provider, and those providers.</returns>
    public static ProviderResolution Resolve(
        IReadOnlyList<string> countries,
        IReadOnlyList<int> selectedProviderIds,
        IReadOnlyDictionary<string, IReadOnlyList<ProviderRef>> flatrateByCountry)
    {
        var available = new Dictionary<string, IReadOnlyList<ProviderRef>>(flatrateByCountry, StringComparer.OrdinalIgnoreCase);

        foreach (var country in countries)
        {
            if (!available.TryGetValue(country, out var offered))
            {
                continue;
            }

            var providers = selectedProviderIds
                .Select(id => offered.FirstOrDefault(p => p.Id == id))
                .OfType<ProviderRef>()
                .ToList();
            if (providers.Count > 0)
            {
                return new ProviderResolution(country.ToUpperInvariant(), providers);
            }
        }

        return new ProviderResolution(null, []);
    }
}
