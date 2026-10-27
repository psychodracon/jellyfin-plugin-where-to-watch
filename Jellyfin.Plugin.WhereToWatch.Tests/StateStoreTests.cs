using Jellyfin.Plugin.WhereToWatch.State;

namespace Jellyfin.Plugin.WhereToWatch.Tests;

public sealed class StateStoreTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 27, 3, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Directory.CreateTempSubdirectory("wtw-state-").FullName;
    private readonly FakeClock _clock = new(Start);

    private string StatePath => Path.Combine(_dir, "state.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Recorded_item_survives_save_and_reload()
    {
        var id = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.Record(new ItemState(id, 603, "PL", [8, 1899], ItemStatus.Resolved, null));
        store.Save();

        var reloaded = new StateStore(StatePath, _clock).Get(id);

        Assert.NotNull(reloaded);
        Assert.Equal(603, reloaded.TmdbId);
        Assert.Equal("PL", reloaded.Country);
        Assert.Equal([8, 1899], reloaded.ProviderIds);
        Assert.Equal(ItemStatus.Resolved, reloaded.Status);
        Assert.Equal(Start, reloaded.LastChecked);
    }

    [Fact]
    public void Item_needs_refresh_when_unknown_or_older_than_the_ttl()
    {
        var ttl = TimeSpan.FromDays(7);
        var known = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.Record(new ItemState(known, 603, "PL", [8], ItemStatus.Resolved, null));

        Assert.True(store.NeedsRefresh(Guid.NewGuid(), ttl));
        Assert.False(store.NeedsRefresh(known, ttl));

        _clock.Now = Start.AddDays(6);
        Assert.False(store.NeedsRefresh(known, ttl));

        _clock.Now = Start.AddDays(8);
        Assert.True(store.NeedsRefresh(known, ttl));
    }

    [Fact]
    public void Interrupted_run_resumes_and_skips_items_it_already_handled()
    {
        var ttl = TimeSpan.FromDays(7);
        var handled = Guid.NewGuid();
        var pending = Guid.NewGuid();
        var first = new StateStore(StatePath, _clock);
        var runId = first.StartRun(force: true);
        first.Record(new ItemState(handled, 1, "PL", [8], ItemStatus.Resolved, null));
        first.Save();

        var resumed = new StateStore(StatePath, _clock);
        var resumedRunId = resumed.StartRun(force: true);

        Assert.Equal(runId, resumedRunId);
        Assert.False(resumed.NeedsRefresh(handled, ttl));
        Assert.True(resumed.NeedsRefresh(pending, ttl));
    }

    [Fact]
    public void Completed_run_is_not_resumed_and_a_forced_run_refreshes_everything_again()
    {
        var ttl = TimeSpan.FromDays(7);
        var item = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        var firstRun = store.StartRun(force: true);
        store.Record(new ItemState(item, 1, "PL", [8], ItemStatus.Resolved, null));
        store.CompleteRun();
        store.Save();

        var next = new StateStore(StatePath, _clock);
        var nextRun = next.StartRun(force: true);

        Assert.NotEqual(firstRun, nextRun);
        Assert.True(next.NeedsRefresh(item, ttl));
    }

    [Fact]
    public void Corrupt_state_file_is_treated_as_empty_instead_of_failing()
    {
        File.WriteAllText(StatePath, "{ \"items\": [ { \"itemId\": ");

        var store = new StateStore(StatePath, _clock);

        Assert.True(store.NeedsRefresh(Guid.NewGuid(), TimeSpan.FromDays(7)));
    }

    [Fact]
    public void Save_replaces_the_file_and_leaves_no_temporary_files_behind()
    {
        var store = new StateStore(StatePath, _clock);
        store.Record(new ItemState(Guid.NewGuid(), 1, "PL", [8], ItemStatus.Resolved, null));
        store.Save();
        store.Record(new ItemState(Guid.NewGuid(), 2, "PL", [8], ItemStatus.Resolved, null));
        store.Save();

        Assert.Equal([StatePath], Directory.GetFiles(_dir));
        Assert.Equal(2, new StateStore(StatePath, _clock).Count);
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
