using Jellyfin.Plugin.WhereToWatch.Rendering;
using Jellyfin.Plugin.WhereToWatch.Scanning;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.WhereToWatch.ScheduledTasks;

/// <summary>
/// Manual task that discards cached renders and re-checks every item regardless of age.
/// </summary>
public sealed class HardRefreshTask : IScheduledTask
{
    private readonly ProviderScanner _scanner;
    private readonly RenderCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="HardRefreshTask"/> class.
    /// </summary>
    /// <param name="scanner">Provider scanner.</param>
    /// <param name="cache">Render cache.</param>
    public HardRefreshTask(ProviderScanner scanner, RenderCache cache)
    {
        _scanner = scanner;
        _cache = cache;
    }

    /// <inheritdoc />
    public string Name => "Where to Watch: hard refresh";

    /// <inheritdoc />
    public string Key => "WhereToWatchHardRefresh";

    /// <inheritdoc />
    public string Description => "Clears rendered posters and re-checks every item with TMDB, ignoring the refresh interval. Does not remove tags.";

    /// <inheritdoc />
    public string Category => "Where to Watch";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _cache.Clear();
        return _scanner.ScanAsync(force: true, progress, cancellationToken);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
