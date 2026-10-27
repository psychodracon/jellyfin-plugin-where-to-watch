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
    private readonly object _gate = new();
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
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>
    /// Gets the recorded state of an item.
    /// </summary>
    /// <param name="itemId">Jellyfin item id.</param>
    /// <returns>The state, or null when the item was never checked.</returns>
    public ItemState? Get(Guid itemId)
    {
        lock (_gate)
        {
            return _items.GetValueOrDefault(itemId);
        }
    }

    /// <summary>
    /// Starts a run, or resumes the previous one if it never completed.
    /// </summary>
    /// <param name="force">Whether the run ignores the ttl and re-checks every item.</param>
    /// <returns>The id of the active run.</returns>
    public string StartRun(bool force)
    {
        lock (_gate)
        {
            // An unforced leftover run must not swallow a forced request, or the ttl would still apply.
            var resumable = _run is { Completed: false } && (_run.Force || !force);
            if (!resumable)
            {
                _run = new RunState(Guid.NewGuid().ToString("N"), force, false);
            }

            return _run!.Id;
        }
    }

    /// <summary>
    /// Marks the active run as finished so the next run starts fresh.
    /// </summary>
    public void CompleteRun()
    {
        lock (_gate)
        {
            if (_run is not null)
            {
                _run = _run with { Completed = true };
            }
        }
    }

    /// <summary>
    /// Records the result of checking an item.
    /// </summary>
    /// <param name="state">The result.</param>
    public void Record(ItemState state)
    {
        lock (_gate)
        {
            _items[state.ItemId] = state with { LastChecked = _clock.GetUtcNow(), RunId = _run?.Id };
        }
    }

    /// <summary>
    /// Decides whether an item must be looked up again.
    /// </summary>
    /// <param name="itemId">Jellyfin item id.</param>
    /// <param name="ttl">How long a lookup stays valid.</param>
    /// <returns>True when the item was not handled by the active run and is unknown, stale or forced.</returns>
    public bool NeedsRefresh(Guid itemId, TimeSpan ttl)
    {
        lock (_gate)
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
    }

    /// <summary>
    /// Summarizes the recorded state for reporting.
    /// </summary>
    /// <returns>Counts by status and run information.</returns>
    public StateSummary Summary()
    {
        lock (_gate)
        {
            var byStatus = _items.Values.GroupBy(i => i.Status).ToDictionary(g => g.Key, g => g.Count());
            DateTimeOffset? lastChecked = _items.Count == 0 ? null : _items.Values.Max(i => i.LastChecked);
            return new StateSummary(_items.Count, byStatus, _run?.Id, _run?.Completed ?? false, lastChecked);
        }
    }

    /// <summary>
    /// Writes the state to disk.
    /// </summary>
    public void Save()
    {
        lock (_gate)
        {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new StateFile(_run, _items.Values.ToList()), JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
    }

    private sealed record RunState(string Id, bool Force, bool Completed);

    private sealed record StateFile(RunState? Run, List<ItemState> Items);
}

/// <summary>
/// Overview of the recorded state.
/// </summary>
/// <param name="Total">Number of items with recorded state.</param>
/// <param name="ByStatus">Item counts per status.</param>
/// <param name="RunId">Id of the latest run, if any.</param>
/// <param name="RunCompleted">Whether the latest run finished.</param>
/// <param name="LastChecked">When any item was last checked.</param>
public sealed record StateSummary(
    int Total,
    IReadOnlyDictionary<ItemStatus, int> ByStatus,
    string? RunId,
    bool RunCompleted,
    DateTimeOffset? LastChecked);
