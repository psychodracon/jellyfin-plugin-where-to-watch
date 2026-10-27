using System.Diagnostics;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WhereToWatch.Configuration;
using Jellyfin.Plugin.WhereToWatch.Logos;
using Jellyfin.Plugin.WhereToWatch.Rendering;
using Jellyfin.Plugin.WhereToWatch.Resolution;
using Jellyfin.Plugin.WhereToWatch.State;
using Jellyfin.Plugin.WhereToWatch.Tags;
using Jellyfin.Plugin.WhereToWatch.Tmdb;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WhereToWatch.Scanning;

/// <summary>
/// Resolves providers for library items and applies tags.
/// </summary>
public sealed class ProviderScanner
{
    private const int CheckpointItems = 200;
    private const long MaxCacheBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(30);

    private readonly ILibraryManager _libraryManager;
    private readonly TmdbClient _tmdb;
    private readonly LogoStore _logos;
    private readonly StateStore _state;
    private readonly RenderCache _cache;
    private readonly ILogger<ProviderScanner> _logger;
    private readonly SemaphoreSlim _running = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderScanner"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="tmdb">TMDB client.</param>
    /// <param name="logos">Logo store.</param>
    /// <param name="state">State store.</param>
    /// <param name="cache">Render cache, pruned after each run.</param>
    /// <param name="logger">Logger.</param>
    public ProviderScanner(ILibraryManager libraryManager, TmdbClient tmdb, LogoStore logos, StateStore state, RenderCache cache, ILogger<ProviderScanner> logger)
    {
        _libraryManager = libraryManager;
        _tmdb = tmdb;
        _logos = logos;
        _state = state;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Looks up providers for every item that needs it and applies tags.
    /// </summary>
    /// <param name="force">Ignore the ttl and re-check every item.</param>
    /// <param name="progress">Progress reporter, 0-100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the run ends.</returns>
    public async Task ScanAsync(bool force, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        if (string.IsNullOrWhiteSpace(config.TmdbApiKey) || config.SelectedProviderIds.Length == 0)
        {
            _logger.LogWarning("Where to Watch: set a TMDB API key and select at least one provider before scanning");
            return;
        }

        if (!await _running.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("Where to Watch: a scan is already running");
            return;
        }

        try
        {
            _state.StartRun(force, config.ResolutionFingerprint());
            await CacheSelectedLogosAsync(config, cancellationToken).ConfigureAwait(false);

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
                IsVirtualItem = false,
                Recursive = true,
            });
            _state.SetRunTotal(items.Count);

            var ttl = TimeSpan.FromDays(Math.Max(1, config.TtlDays));
            var handled = 0;
            var sinceCheckpoint = 0;
            var checkpointClock = Stopwatch.StartNew();
            var gate = new object();

            try
            {
                await Parallel.ForEachAsync(
                    items,
                    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
                    async (item, token) =>
                    {
                        if (_state.NeedsRefresh(item.Id, ttl))
                        {
                            await ProcessAsync(item, config, token).ConfigureAwait(false);
                        }

                        lock (gate)
                        {
                            handled++;
                            sinceCheckpoint++;
                            progress.Report(100.0 * handled / items.Count);
                            if (sinceCheckpoint >= CheckpointItems || checkpointClock.Elapsed >= CheckpointInterval)
                            {
                                _state.Save();
                                sinceCheckpoint = 0;
                                checkpointClock.Restart();
                            }
                        }
                    }).ConfigureAwait(false);

                _state.CompleteRun();
            }
            finally
            {
                _state.Save();
                _cache.Prune(MaxCacheBytes);
            }
        }
        finally
        {
            _running.Release();
        }
    }

    private async Task CacheSelectedLogosAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        try
        {
            var country = config.PrimaryCountry;
            var providers = await _tmdb.GetProvidersAsync(config.TmdbApiKey, country, cancellationToken).ConfigureAwait(false);
            await _logos.EnsureCachedAsync(providers.Where(p => config.SelectedProviderIds.Contains(p.Id)), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Where to Watch: could not refresh provider logos");
        }
    }

    private async Task ProcessAsync(BaseItem item, PluginConfiguration config, CancellationToken cancellationToken)
    {
        if (!item.TryGetProviderId(MetadataProvider.Tmdb, out var tmdbText) || !int.TryParse(tmdbText, out var tmdbId))
        {
            _state.Record(new ItemState(item.Id, null, null, [], ItemStatus.NoTmdbId, null));
            return;
        }

        try
        {
            var flatrate = await _tmdb.GetFlatrateAsync(config.TmdbApiKey, item is Series, tmdbId, cancellationToken).ConfigureAwait(false);
            if (flatrate is null)
            {
                _state.Record(new ItemState(item.Id, tmdbId, null, [], ItemStatus.NotFound, null));
                if (config.TagsEnabled)
                {
                    await ApplyTagsAsync(item, [], cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            var resolution = CountryResolver.Resolve(config.Countries, config.SelectedProviderIds, flatrate);
            _state.Record(new ItemState(item.Id, tmdbId, resolution.Country, resolution.Providers.Select(p => p.Id).ToList(), ItemStatus.Resolved, null));

            if (config.TagsEnabled)
            {
                await ApplyTagsAsync(item, resolution.Providers.Select(p => p.Name).ToList(), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Where to Watch: lookup failed for {Name}", item.Name);
            _state.Record(new ItemState(item.Id, tmdbId, null, [], ItemStatus.Failed, ex.Message));
        }
    }

    private async Task ApplyTagsAsync(BaseItem item, IReadOnlyList<string> providerNames, CancellationToken cancellationToken)
    {
        await ApplyToItemAsync(item, providerNames, cancellationToken).ConfigureAwait(false);

        // Episodes have no providers of their own on TMDB, so they carry their series' tags.
        if (item is Series)
        {
            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Episode],
                ParentId = item.Id,
                Recursive = true,
            });
            foreach (var episode in episodes)
            {
                await ApplyToItemAsync(episode, providerNames, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task ApplyToItemAsync(BaseItem item, IEnumerable<string> providerNames, CancellationToken cancellationToken)
    {
        var current = item.Tags ?? [];
        var changes = TagDiff.Compute(current, providerNames);
        if (changes.ToAdd.Count == 0 && changes.ToRemove.Count == 0)
        {
            return;
        }

        item.Tags = TagDiff.Apply(current, changes);
        await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
    }
}
