using Jellyfin.Plugin.WhereToWatch.State;
using Jellyfin.Plugin.WhereToWatch.Tmdb;

namespace Jellyfin.Plugin.WhereToWatch.Links;

/// <summary>
/// Fetches and remembers the direct provider links of an item, only when the details page asks for them.
/// </summary>
public sealed class DirectLinkService
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    private readonly TmdbClient _tmdb;
    private readonly StateStore _state;

    // Pages are fetched one at a time, so opening several details pages cannot burst requests at TMDB.
    private readonly SemaphoreSlim _one = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectLinkService"/> class.
    /// </summary>
    /// <param name="tmdb">TMDB client.</param>
    /// <param name="state">State store.</param>
    public DirectLinkService(TmdbClient tmdb, StateStore state)
    {
        _tmdb = tmdb;
        _state = state;
    }

    /// <summary>
    /// Gets the direct links of an item, fetching them first when they were never fetched or have expired.
    /// </summary>
    /// <param name="itemId">Jellyfin item id.</param>
    /// <param name="ttl">How long fetched links stay valid.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Links by TMDB provider id; null when there is nothing to base them on.</returns>
    public async Task<IReadOnlyDictionary<int, string>?> GetAsync(Guid itemId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var state = _state.Get(itemId);
        if (state?.Link is null)
        {
            return null;
        }

        if (!state.LinksDue(DateTimeOffset.UtcNow, ttl, RetryAfter))
        {
            return state.ProviderUrls;
        }

        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another request may have fetched them while this one waited.
            state = _state.Get(itemId);
            if (state?.Link is null)
            {
                return null;
            }

            if (!state.LinksDue(DateTimeOffset.UtcNow, ttl, RetryAfter))
            {
                return state.ProviderUrls;
            }

            var html = await _tmdb.GetWatchPageAsync(state.Link, cancellationToken).ConfigureAwait(false);
            var links = html is null ? new Dictionary<int, string>() : WatchPageParser.StreamLinks(html);
            _state.RecordProviderUrls(itemId, links);
            _state.Save();
            return links;
        }
        finally
        {
            _one.Release();
        }
    }
}
