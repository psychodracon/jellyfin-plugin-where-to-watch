using Jellyfin.Plugin.WhereToWatch.Scanning;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.WhereToWatch.ScheduledTasks;

/// <summary>
/// Daily task that resolves providers and applies tags.
/// </summary>
public sealed class ScanTask : IScheduledTask
{
    private readonly ProviderScanner _scanner;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScanTask"/> class.
    /// </summary>
    /// <param name="scanner">Provider scanner.</param>
    public ScanTask(ProviderScanner scanner)
    {
        _scanner = scanner;
    }

    /// <inheritdoc />
    public string Name => "Where to Watch: scan providers";

    /// <inheritdoc />
    public string Key => "WhereToWatchScan";

    /// <inheritdoc />
    public string Description => "Looks up streaming providers for items that are new or past the refresh interval, and applies stream: tags. Resumes after an interruption.";

    /// <inheritdoc />
    public string Category => "Where to Watch";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _scanner.ScanAsync(force: false, progress, cancellationToken);

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks,
        };
    }
}
