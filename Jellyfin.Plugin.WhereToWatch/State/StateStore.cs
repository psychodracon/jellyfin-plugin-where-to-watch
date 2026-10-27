using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.WhereToWatch.State;

/// <summary>
/// Persists per-item lookup results to a JSON file.
/// </summary>
public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly Dictionary<Guid, ItemState> _items = [];
    private RunState? _run;

    /// <summary>
    /// Initializes a new instance of the <see cref="StateStore"/> class and loads existing state.
    /// </summary>
    /// <param name="path">State file path.</param>
    /// <param name="clock">Clock used to stamp records.</param>
    public StateStore(string path, TimeProvider clock)
    {
        _path = path;
        _clock = clock;

        if (File.Exists(path))
        {
            try
            {
                var file = JsonSerializer.Deserialize<StateFile>(File.ReadAllText(path), JsonOptions);
                _run = file?.Run;
                foreach (var item in file?.Items ?? [])
                {
                    _items[item.ItemId] = item;
                }
            }
            catch (JsonException)
            {
                // A crash mid-write can leave a truncated file; starting empty only costs a re-check.
                _run = null;
                _items.Clear();
            }
        }
    }

    /// <summary>
    /// Gets the number of items with recorded state.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Gets the recorded state of an item.
    /// </summary>
    /// <param name="itemId">Jellyfin item id.</param>
    /// <returns>The state, or null when the item was never checked.</returns>
    public ItemState? Get(Guid itemId) => _items.GetValueOrDefault(itemId);

    /// <summary>
    /// Starts a run, or resumes the previous one if it never completed.
    /// </summary>
    /// <param name="force">Whether the run ignores the ttl and re-checks every item. Ignored when resuming.</param>
    /// <returns>The id of the active run.</returns>
    public string StartRun(bool force)
    {
        if (_run is null || _run.Completed)
        {
            _run = new RunState(Guid.NewGuid().ToString("N"), force, false);
        }

        return _run.Id;
    }

    /// <summary>
    /// Marks the active run as finished so the next run starts fresh.
    /// </summary>
    public void CompleteRun()
    {
        if (_run is not null)
        {
            _run = _run with { Completed = true };
        }
    }

    /// <summary>
    /// Records the result of checking an item.
    /// </summary>
    /// <param name="state">The result.</param>
    public void Record(ItemState state)
    {
        _items[state.ItemId] = state with { LastChecked = _clock.GetUtcNow(), RunId = _run?.Id };
    }

    /// <summary>
    /// Decides whether an item must be looked up again.
    /// </summary>
    /// <param name="itemId">Jellyfin item id.</param>
    /// <param name="ttl">How long a lookup stays valid.</param>
    /// <returns>True when the item was not handled by the active run and is unknown, stale or forced.</returns>
    public bool NeedsRefresh(Guid itemId, TimeSpan ttl)
    {
        var activeRun = _run is { Completed: false } ? _run : null;
        if (!_items.TryGetValue(itemId, out var state))
        {
            return true;
        }

        if (activeRun is not null)
        {
            if (state.RunId == activeRun.Id)
            {
                return false;
            }

            if (activeRun.Force)
            {
                return true;
            }
        }

        return _clock.GetUtcNow() - state.LastChecked > ttl;
    }

    /// <summary>
    /// Writes the state to disk.
    /// </summary>
    public void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new StateFile(_run, _items.Values.ToList()), JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }

    private sealed record RunState(string Id, bool Force, bool Completed);

    private sealed record StateFile(RunState? Run, List<ItemState> Items);
}
