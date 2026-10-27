using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WhereToWatch.Tags;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.WhereToWatch.ScheduledTasks;

/// <summary>
/// Manual task that removes every stream: tag, for a clean uninstall.
/// </summary>
public sealed class RemoveTagsTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveTagsTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    public RemoveTagsTask(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public string Name => "Where to Watch: remove all stream: tags";

    /// <inheritdoc />
    public string Key => "WhereToWatchRemoveTags";

    /// <inheritdoc />
    public string Description => "Removes every stream: tag this plugin added. Other tags are left alone.";

    /// <inheritdoc />
    public string Category => "Where to Watch";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode],
            Recursive = true,
        });

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[i];
            var tags = item.Tags ?? [];
            if (tags.Any(t => t.StartsWith(TagDiff.Prefix, StringComparison.OrdinalIgnoreCase)))
            {
                item.Tags = tags.Where(t => !t.StartsWith(TagDiff.Prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
                await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            }

            progress.Report(100.0 * (i + 1) / items.Count);
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
