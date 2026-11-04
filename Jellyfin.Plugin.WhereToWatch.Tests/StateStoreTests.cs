using Jellyfin.Plugin.WhereToWatch.State;
using Jellyfin.Plugin.WhereToWatch.Tmdb;

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

    [Fact]
    public void Summary_counts_items_by_status_and_reports_the_run()
    {
        var store = new StateStore(StatePath, _clock);
        var runId = store.StartRun(force: false);
        store.Record(new ItemState(Guid.NewGuid(), 1, "PL", [8], ItemStatus.Resolved, null));
        store.Record(new ItemState(Guid.NewGuid(), 2, "PL", [8], ItemStatus.Resolved, null));
        store.Record(new ItemState(Guid.NewGuid(), 3, null, [], ItemStatus.Failed, "HTTP 500"));

        var summary = store.Summary();

        Assert.Equal(3, summary.Total);
        Assert.Equal(2, summary.ByStatus[ItemStatus.Resolved]);
        Assert.Equal(1, summary.ByStatus[ItemStatus.Failed]);
        Assert.Equal(runId, summary.RunId);
        Assert.False(summary.RunCompleted);
        Assert.Equal(Start, summary.LastChecked);

        store.CompleteRun();
        Assert.True(store.Summary().RunCompleted);
    }

    [Fact]
    public void Forced_request_does_not_resume_an_unfinished_unforced_run()
    {
        var store = new StateStore(StatePath, _clock);
        var unforced = store.StartRun(force: false);
        store.Save();

        var reloaded = new StateStore(StatePath, _clock);
        var forced = reloaded.StartRun(force: true);

        Assert.NotEqual(unforced, forced);
    }

    [Fact]
    public void Failed_item_is_retried_on_the_next_run_but_not_again_within_the_same_run()
    {
        var ttl = TimeSpan.FromDays(7);
        var item = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.StartRun(force: false);
        store.Record(new ItemState(item, 1, null, [], ItemStatus.Failed, "HTTP 429"));

        Assert.False(store.NeedsRefresh(item, ttl));

        store.CompleteRun();
        store.StartRun(force: false);
        Assert.True(store.NeedsRefresh(item, ttl));
    }

    [Fact]
    public void Expire_makes_every_item_due_again_while_keeping_what_was_resolved()
    {
        var item = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.Record(new ItemState(item, 1, "PL", [8], ItemStatus.Resolved, null));
        Assert.False(store.NeedsRefresh(item, TimeSpan.FromDays(7)));

        store.Expire();

        Assert.True(store.NeedsRefresh(item, TimeSpan.FromDays(7)));
        Assert.Equal([8], store.Get(item)!.ProviderIds);
    }

    [Fact]
    public void Changed_settings_fingerprint_forces_a_full_recheck_once()
    {
        var ttl = TimeSpan.FromDays(7);
        var item = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.StartRun(force: false, fingerprint: "PL|8");
        store.Record(new ItemState(item, 1, "PL", [8], ItemStatus.Resolved, null));
        store.CompleteRun();
        store.Save();

        var sameSettings = new StateStore(StatePath, _clock);
        sameSettings.StartRun(force: false, fingerprint: "PL|8");
        Assert.False(sameSettings.NeedsRefresh(item, ttl));
        sameSettings.CompleteRun();
        sameSettings.Save();

        var changed = new StateStore(StatePath, _clock);
        changed.StartRun(force: false, fingerprint: "PL|8,337");
        Assert.True(changed.NeedsRefresh(item, ttl));
        changed.Record(new ItemState(item, 1, "PL", [8, 337], ItemStatus.Resolved, null));
        changed.CompleteRun();
        changed.Save();

        var settled = new StateStore(StatePath, _clock);
        settled.StartRun(force: false, fingerprint: "PL|8,337");
        Assert.False(settled.NeedsRefresh(item, ttl));
    }

    [Fact]
    public void Summary_reports_run_progress()
    {
        var store = new StateStore(StatePath, _clock);
        store.StartRun(force: false);
        store.SetRunTotal(10);
        store.Record(new ItemState(Guid.NewGuid(), 1, "PL", [8], ItemStatus.Resolved, null));
        store.Record(new ItemState(Guid.NewGuid(), 2, "PL", [8], ItemStatus.Resolved, null));

        var summary = store.Summary();

        Assert.Equal(2, summary.RunHandled);
        Assert.Equal(10, summary.RunTotal);
    }

    [Fact]
    public void Watch_page_link_survives_save_and_reload()
    {
        var id = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.Record(new ItemState(id, 1, "PL", [8], ItemStatus.Resolved, null) { Link = "https://www.themoviedb.org/movie/1/watch?locale=PL" });
        store.Save();

        var reloaded = new StateStore(StatePath, _clock).Get(id);

        Assert.Equal("https://www.themoviedb.org/movie/1/watch?locale=PL", reloaded!.Link);
    }

    [Fact]
    public void Items_recorded_without_a_link_reload_without_one()
    {
        var id = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.Record(new ItemState(id, 1, "PL", [8], ItemStatus.Resolved, null));
        store.Save();

        Assert.Null(new StateStore(StatePath, _clock).Get(id)!.Link);
    }

    [Fact]
    public void Recording_again_replaces_the_previous_link()
    {
        var id = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.Record(new ItemState(id, 1, "PL", [8], ItemStatus.Resolved, null) { Link = "https://example.test/old" });
        store.Record(new ItemState(id, 1, "US", [8], ItemStatus.Resolved, null));

        Assert.Null(store.Get(id)!.Link);
    }

    [Fact]
    public void Provider_names_survive_save_and_reload_in_step_with_the_ids()
    {
        var id = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.Record(new ItemState(id, 1, "PL", [8, 1899], ItemStatus.Resolved, null) { ProviderNames = ["Netflix", "HBO Max"] });
        store.Save();

        var reloaded = new StateStore(StatePath, _clock).Get(id);

        Assert.Equal(["Netflix", "HBO Max"], reloaded!.ProviderNames);
    }

    [Fact]
    public void Items_saved_before_names_existed_load_with_an_empty_name_list()
    {
        File.WriteAllText(StatePath, "{\"Items\":[{\"ItemId\":\"" + Guid.Empty + "\",\"TmdbId\":1,\"Country\":\"PL\",\"ProviderIds\":[8],\"Status\":\"Resolved\",\"Error\":null,\"LastChecked\":\"2026-10-27T03:00:00+00:00\",\"RunId\":null}]}");

        var loaded = new StateStore(StatePath, _clock).Get(Guid.Empty);

        Assert.Empty(loaded!.ProviderNames);
    }

    [Fact]
    public void Stream_links_survive_save_and_reload_with_their_names()
    {
        var id = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.Record(new ItemState(id, 1, "PL", [8, 337], ItemStatus.Resolved, null) { Link = "https://www.themoviedb.org/movie/1/watch?locale=PL" });
        store.RecordStreamLinks(id, [new StreamLink(8, "Netflix", "https://click.example/netflix"), new StreamLink(2706, "Disney Plus", "https://click.example/disney")]);
        store.Save();

        var reloaded = new StateStore(StatePath, _clock).Get(id);

        Assert.Equal(new StreamLink(8, "Netflix", "https://click.example/netflix"), reloaded!.StreamLinks![0]);
        Assert.Equal(new StreamLink(2706, "Disney Plus", "https://click.example/disney"), reloaded.StreamLinks[1]);
        Assert.Equal(Start, reloaded.LinksFetched);
    }

    [Fact]
    public void Rescanning_an_item_keeps_its_stream_links_while_the_watch_page_is_unchanged()
    {
        var id = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.Record(new ItemState(id, 1, "PL", [8], ItemStatus.Resolved, null) { Link = "https://tmdb.test/watch?locale=PL" });
        store.RecordStreamLinks(id, [new StreamLink(8, "Netflix", "https://click.example/netflix")]);

        store.Record(new ItemState(id, 1, "PL", [8], ItemStatus.Resolved, null) { Link = "https://tmdb.test/watch?locale=PL" });

        Assert.Equal("https://click.example/netflix", store.Get(id)!.StreamLinks![0].Url);
        Assert.Equal(Start, store.Get(id)!.LinksFetched);
    }

    [Fact]
    public void Rescanning_an_item_drops_its_stream_links_when_the_watch_page_changed()
    {
        var id = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.Record(new ItemState(id, 1, "PL", [8], ItemStatus.Resolved, null) { Link = "https://tmdb.test/watch?locale=PL" });
        store.RecordStreamLinks(id, [new StreamLink(8, "Netflix", "https://click.example/netflix")]);

        store.Record(new ItemState(id, 1, "US", [8], ItemStatus.Resolved, null) { Link = "https://tmdb.test/watch?locale=US" });

        Assert.Null(store.Get(id)!.StreamLinks);
        Assert.Null(store.Get(id)!.LinksFetched);
    }

    [Fact]
    public void Stream_links_for_an_unknown_item_are_ignored()
    {
        var store = new StateStore(StatePath, _clock);

        store.RecordStreamLinks(Guid.NewGuid(), [new StreamLink(8, "Netflix", "https://click.example/netflix")]);

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void State_saved_with_the_earlier_link_format_loads_without_links_and_is_due_for_a_fetch()
    {
        // Version 1.2.0 stored links by id only, under different names; they cannot be matched by name, so they are fetched again.
        File.WriteAllText(
            StatePath,
            "{\"Items\":[{\"ItemId\":\"" + Guid.Empty + "\",\"TmdbId\":1,\"Country\":\"PL\",\"ProviderIds\":[337],\"Status\":\"Resolved\",\"Error\":null,\"LastChecked\":\"2026-10-27T03:00:00+00:00\",\"RunId\":null,\"Link\":\"https://tmdb.test/x\",\"ProviderUrls\":{\"337\":\"https://old\"},\"UrlsChecked\":\"2026-10-27T03:00:00+00:00\"}]}");

        var loaded = new StateStore(StatePath, _clock).Get(Guid.Empty)!;

        Assert.Null(loaded.StreamLinks);
        Assert.True(loaded.LinksDue(Start, TimeSpan.FromDays(7), TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Stream_links_are_due_when_never_fetched_and_again_after_the_interval()
    {
        var ttl = TimeSpan.FromDays(7);
        var retry = TimeSpan.FromHours(1);
        var never = new ItemState(Guid.NewGuid(), 1, "PL", [8], ItemStatus.Resolved, null);
        var fetched = never with { StreamLinks = [new StreamLink(8, "Netflix", "https://x.test")], LinksFetched = Start };

        Assert.True(never.LinksDue(Start, ttl, retry));
        Assert.False(fetched.LinksDue(Start.AddDays(6), ttl, retry));
        Assert.True(fetched.LinksDue(Start.AddDays(8), ttl, retry));
    }

    [Fact]
    public void An_empty_result_is_retried_after_the_short_delay_not_the_full_interval()
    {
        var ttl = TimeSpan.FromDays(7);
        var retry = TimeSpan.FromHours(1);
        var nothing = new ItemState(Guid.NewGuid(), 1, "PL", [8], ItemStatus.Resolved, null) { StreamLinks = [], LinksFetched = Start };

        Assert.False(nothing.LinksDue(Start.AddMinutes(30), ttl, retry));
        Assert.True(nothing.LinksDue(Start.AddMinutes(90), ttl, retry));
    }

    [Fact]
    public void A_failed_lookup_keeps_what_was_resolved_for_the_same_title()
    {
        var id = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.Record(new ItemState(id, 603, "PL", [8, 1899], ItemStatus.Resolved, null) { Link = "https://tmdb/page", ProviderNames = ["Netflix", "Max"] });

        store.RecordFailure(id, 603, "timeout");

        var state = store.Get(id)!;
        Assert.Equal(ItemStatus.Failed, state.Status);
        Assert.Equal("timeout", state.Error);
        Assert.Equal([8, 1899], state.ProviderIds);
        Assert.Equal("PL", state.Country);
        Assert.Equal("https://tmdb/page", state.Link);
        Assert.True(store.NeedsRefresh(id, TimeSpan.FromDays(7)));
    }

    [Fact]
    public void A_failed_lookup_drops_old_results_when_the_tmdb_id_changed()
    {
        var id = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.Record(new ItemState(id, 603, "PL", [8], ItemStatus.Resolved, null));

        store.RecordFailure(id, 604, "timeout");

        var state = store.Get(id)!;
        Assert.Equal(ItemStatus.Failed, state.Status);
        Assert.Equal(604, state.TmdbId);
        Assert.Empty(state.ProviderIds);
    }

    [Fact]
    public void A_failed_lookup_of_an_unknown_item_is_recorded()
    {
        var id = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);

        store.RecordFailure(id, 1, "HTTP 500");

        Assert.Equal(ItemStatus.Failed, store.Get(id)!.Status);
    }

    [Fact]
    public void Save_does_not_block_readers_and_round_trips_under_concurrent_use()
    {
        var store = new StateStore(StatePath, _clock);
        var ids = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToList();

        Parallel.ForEach(ids, id =>
        {
            store.Record(new ItemState(id, 1, "PL", [8], ItemStatus.Resolved, null));
            store.Save();
            _ = store.Get(id);
        });
        store.Save();

        Assert.Equal(ids.Count, new StateStore(StatePath, _clock).Count);
    }

    [Fact]
    public async Task Scheduled_saves_are_batched_into_a_write_later()
    {
        var id = Guid.NewGuid();
        var store = new StateStore(StatePath, _clock);
        store.Record(new ItemState(id, 1, "PL", [8], ItemStatus.Resolved, null));

        store.ScheduleSave();
        store.ScheduleSave();
        Assert.False(File.Exists(StatePath));

        for (var i = 0; i < 150 && !File.Exists(StatePath); i++)
        {
            await Task.Delay(100);
        }

        Assert.True(File.Exists(StatePath));
        Assert.NotNull(new StateStore(StatePath, _clock).Get(id));
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
