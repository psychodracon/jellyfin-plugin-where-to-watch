using System.Globalization;
using Jellyfin.Plugin.WhereToWatch.Tmdb;

namespace Jellyfin.Plugin.WhereToWatch.Logos;

/// <summary>
/// Finds provider logos: a custom file named after the provider id wins, otherwise the cached TMDB icon.
/// </summary>
public sealed class LogoStore
{
    private static readonly string[] CustomExtensions = [".png", ".webp", ".jpg", ".jpeg"];

    private readonly TmdbClient _tmdb;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogoStore"/> class.
    /// </summary>
    /// <param name="tmdb">TMDB client used to fetch fallback icons.</param>
    public LogoStore(TmdbClient tmdb)
    {
        _tmdb = tmdb;
    }

    private static string Root => Path.Combine(Plugin.Instance!.DataFolderPath, "logos");

    private static string CacheRoot => Path.Combine(Root, "cache");

    /// <summary>
    /// Loads the logo for a provider.
    /// </summary>
    /// <param name="providerId">TMDB provider id.</param>
    /// <returns>The encoded logo, or null when there is none.</returns>
    public byte[]? Load(int providerId)
    {
        var path = Resolve(providerId);
        return path is null ? null : File.ReadAllBytes(path);
    }

    /// <summary>
    /// Gets a token that changes when the logo file for a provider changes.
    /// </summary>
    /// <param name="providerId">TMDB provider id.</param>
    /// <returns>A token for cache keys; empty when there is no logo.</returns>
    public string Stamp(int providerId)
    {
        var path = Resolve(providerId);
        return path is null ? string.Empty : File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Downloads missing fallback icons for the given providers.
    /// </summary>
    /// <param name="providers">Providers whose icons should exist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when downloads finish.</returns>
    public async Task EnsureCachedAsync(IEnumerable<TmdbProvider> providers, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(CacheRoot);
        foreach (var provider in providers)
        {
            if (provider.LogoPath is null || FindCustom(provider.Id) is not null || File.Exists(CachedPath(provider.Id)))
            {
                continue;
            }

            var bytes = await _tmdb.DownloadLogoAsync(provider.LogoPath, cancellationToken).ConfigureAwait(false);
            if (bytes is not null)
            {
                await File.WriteAllBytesAsync(CachedPath(provider.Id), bytes, cancellationToken).ConfigureAwait(false);

                // Only now is it safe to drop the 154px icon an earlier version cached.
                File.Delete(LegacyCachedPath(provider.Id));
            }
        }
    }

    private static string CachedPath(int providerId) => Path.Combine(CacheRoot, providerId.ToString(CultureInfo.InvariantCulture) + ".original.img");

    // Earlier versions cached 154px icons as "<id>.img". They stay in use until the full-size icon has been downloaded.
    private static string LegacyCachedPath(int providerId) => Path.Combine(CacheRoot, providerId.ToString(CultureInfo.InvariantCulture) + ".img");

    private static string? FindCustom(int providerId)
    {
        var name = providerId.ToString(CultureInfo.InvariantCulture);
        return CustomExtensions.Select(extension => Path.Combine(Root, name + extension)).FirstOrDefault(File.Exists);
    }

    private static string? Resolve(int providerId)
    {
        return FindCustom(providerId)
            ?? new[] { CachedPath(providerId), LegacyCachedPath(providerId) }.FirstOrDefault(File.Exists);
    }
}
