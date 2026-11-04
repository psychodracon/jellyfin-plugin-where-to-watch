using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.WhereToWatch.Tmdb;

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

    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(10);

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly object _saveGate = new();
    private readonly Dictionary<Guid, ItemState> _items = [];
    private RunState? _run;
    private Timer? _saveTimer;

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
    /// <param name="fingerprint">Fingerprint of the settings that decide what an item resolves to; a change forces a full re-check.</param>
    /// <returns>The id of the active run.</returns>
    public string StartRun(bool force, string fingerprint = "")
    {
        lock (_gate)
        {
            var settingsChanged = _run is not null && _run.Fingerprint != fingerprint;
            force |= settingsChanged;

            // An unforced leftover run must not swallow a forced request, or the ttl would still apply.
            var resumable = _run is { Completed: false } && !settingsChanged && (_run.Force || !force);
            if (!resumable)
            {
                _run = new RunState(Guid.NewGuid().ToString("N"), force, false, fingerprint, 0);
            }

            return _run!.Id;
        }
    }

    /// <summary>
    /// Records how many items the active run will go through, for progress reporting.
    /// </summary>
    /// <param name="total">Number of items in the run.</param>
    public void SetRunTotal(int total)
    {
        lock (_gate)
        {
            if (_run is not null)
            {
                _run = _run with { Total = total };
            }
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
            // Direct links belong to one watch page; keep them across a rescan only while the page is the same.
            if (state.StreamLinks is null && _items.TryGetValue(state.ItemId, out var previous) && previous.Link is not null && previous.Link == state.Link)
            {
                state = state with { StreamLinks = previous.StreamLinks, LinksFetched = previous.LinksFetched };
            }

            _items[state.ItemId] = state with { LastChecked = _clock.GetUtcNow(), RunId = _run?.Id };
        }
    }

    /// <summary>
    /// Records a failed lookup. What was resolved earlier for the same title is kept, so a TMDB outage does not take the badges away;
    /// the item is marked failed so the next scan retries it.
    /// </summary>
    /// <param name="itemId">Jellyfin item id.</param>
    /// <param name="tmdbId">The TMDB id that was looked up.</param>
    /// <param name="error">What went wrong.</param>
    public void RecordFailure(Guid itemId, int? tmdbId, string error)
    {
        lock (_gate)
        {
            var failed = _items.TryGetValue(itemId, out var previous) && previous.TmdbId == tmdbId && previous.ProviderIds.Count > 0
                ? previous with { Status = ItemStatus.Failed, Error = error }
                : new ItemState(itemId, tmdbId, null, [], ItemStatus.Failed, error);
            _items[itemId] = failed with { LastChecked = _clock.GetUtcNow(), RunId = _run?.Id };
        }
    }

    /// <summary>
    /// Stores the stream links fetched for an item.
    /// </summary>
    /// <param name="itemId">Jellyfin item id.</param>
    /// <param name="links">The links; empty when none were found.</param>
    public void RecordStreamLinks(Guid itemId, IReadOnlyList<StreamLink> links)
    {
        lock (_gate)
        {
            if (_items.TryGetValue(itemId, out var state))
            {
                _items[itemId] = state with { StreamLinks = links, LinksFetched = _clock.GetUtcNow() };
            }
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

            return state.Status == ItemStatus.Failed || _clock.GetUtcNow() - state.LastChecked > ttl;
        }
    }

    /// <summary>
    /// Makes every item due for a new check while keeping what was resolved, so badges keep working until the next scan.
    /// </summary>
    public void Expire()
    {
        lock (_gate)
        {
            foreach (var (id, state) in _items.ToList())
            {
                _items[id] = state with { LastChecked = DateTimeOffset.MinValue, RunId = null };
            }
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
            var handled = _run is null ? 0 : _items.Values.Count(i => i.RunId == _run.Id);
            return new StateSummary(_items.Count, byStatus, _run?.Id, _run?.Completed ?? false, lastChecked, handled, _run?.Total ?? 0);
        }
    }

    /// <summary>
    /// Writes the state to disk.
    /// </summary>
    public void Save()
    {
        // Snapshot under the state lock, but serialise and write outside it so readers are not blocked by disk I/O.
        StateFile snapshot;
        lock (_gate)
        {
            snapshot = new StateFile(_run, _items.Values.ToList());
        }

        lock (_saveGate)
        {
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
    }

    /// <summary>
    /// Asks for the state to be written soon. Many calls close together share one write.
    /// </summary>
    public void ScheduleSave()
    {
        lock (_gate)
        {
            _saveTimer ??= new Timer(_ => SaveScheduled(), null, SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void SaveScheduled()
    {
        lock (_gate)
        {
            _saveTimer?.Dispose();
            _saveTimer = null;
        }

        try
        {
            Save();
        }
        catch (IOException)
        {
            // The next scheduled or checkpoint save will try again.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record RunState(string Id, bool Force, bool Completed, string Fingerprint = "", int Total = 0);

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
/// <param name="RunHandled">Items the latest run has handled so far.</param>
/// <param name="RunTotal">Items the latest run will go through.</param>
public sealed record StateSummary(
    int Total,
    IReadOnlyDictionary<ItemStatus, int> ByStatus,
    string? RunId,
    bool RunCompleted,
    DateTimeOffset? LastChecked,
    int RunHandled,
    int RunTotal);
