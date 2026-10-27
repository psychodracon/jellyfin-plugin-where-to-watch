using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WhereToWatch.State;
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
    private readonly StateStore _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveTagsTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="state">State store, expired afterwards so the next scan tags items again.</param>
    public RemoveTagsTask(ILibraryManager libraryManager, StateStore state)
    {
        _libraryManager = libraryManager;
        _state = state;
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
            if (tags.Any(TagDiff.IsManaged))
            {
                item.Tags = tags.Where(t => !TagDiff.IsManaged(t)).ToArray();
                await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            }

            progress.Report(100.0 * (i + 1) / items.Count);
        }

        // Without this the daily scan would consider items fresh and not tag them again until their refresh interval passes.
        _state.Expire();
        _state.Save();
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
