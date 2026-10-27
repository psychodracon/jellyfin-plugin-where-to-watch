namespace Jellyfin.Plugin.WhereToWatch.State;

/// <summary>
/// How far an item got in the last check.
/// </summary>
public enum ItemStatus
{
    /// <summary>Providers were looked up and recorded.</summary>
    Resolved,

    /// <summary>The lookup failed; see the error text.</summary>
    Failed,

    /// <summary>The item has no TMDB id and cannot be looked up.</summary>
    NoTmdbId,

    /// <summary>TMDB does not know the title.</summary>
    NotFound,
}

/// <summary>
/// What the plugin knows about one library item.
/// </summary>
/// <param name="ItemId">Jellyfin item id.</param>
/// <param name="TmdbId">TMDB id used for the lookup.</param>
/// <param name="Country">Country the providers were resolved in.</param>
/// <param name="ProviderIds">Selected providers the item is on, in priority order.</param>
/// <param name="Status">Outcome of the last check.</param>
/// <param name="Error">Error text when the check failed.</param>
public sealed record ItemState(
    Guid ItemId,
    int? TmdbId,
    string? Country,
    IReadOnlyList<int> ProviderIds,
    ItemStatus Status,
    string? Error)
{
    /// <summary>
    /// Gets when the item was last checked. Stamped by <see cref="StateStore"/>.
    /// </summary>
    public DateTimeOffset LastChecked { get; init; }

    /// <summary>
    /// Gets the run that last handled the item. Stamped by <see cref="StateStore"/>.
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Gets the TMDB watch page (powered by JustWatch) for this title in <see cref="Country"/>, when TMDB provides one.
    /// </summary>
    public string? Link { get; init; }

    /// <summary>
    /// Gets the names of <see cref="ProviderIds"/>, in the same order. Empty for items checked before names were stored.
    /// </summary>
    public IReadOnlyList<string> ProviderNames { get; init; } = [];
}
