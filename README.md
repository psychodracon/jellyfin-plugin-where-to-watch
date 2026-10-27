# Where to Watch

A Jellyfin 12 plugin that finds which streaming services carry your Movies and Series (via TMDB), tags them, and draws provider badges on posters, Kometa-style. Stored images are never modified: badges are drawn as posters are served.

## What it does

- **Tags** items with `stream:<provider>` (for example `stream:Netflix`). Only `stream:*` tags are ever added or removed.
- **Badges** up to N provider logos stacked on the Primary poster of Movies and Series. Position, size, margin, spacing, backdrop colour, corner radius and JPEG quality are configurable.
- Uses **flatrate** (subscription) availability only, with an **ordered country fallback** (for example `PL`, then `GB`): the first country where a selected provider has the title is used.
- **State and resume:** per-item results are stored in `<plugin data>/state.json`, written atomically and checkpointed during a run. An interrupted run resumes where it stopped; items are re-checked after a configurable number of days.

## How badges are drawn

A small middleware intercepts `Items/{id}/Images/Primary` requests for items that have providers in the state, draws the badges on the poster Jellyfin would have served, and caches the result under Jellyfin's cache folder. Disabling the plugin or the badges brings the original posters straight back.

Logo lookup order per provider id:

1. `<plugin data>/logos/<providerId>.png` (also `.webp`, `.jpg`, `.jpeg`)
2. the TMDB icon, downloaded to `<plugin data>/logos/cache/`

Provider ids are shown next to each provider on the configuration page.

## Scheduled tasks

| Task | Runs | What it does |
| --- | --- | --- |
| Where to Watch: scan providers | daily 03:00 | Checks new items and items past the refresh interval; applies tags. Resumes after an interruption. |
| Where to Watch: hard refresh | manual | Clears rendered posters and re-checks every item, ignoring the refresh interval. Does not remove tags. |
| Where to Watch: remove all stream: tags | manual | Removes every `stream:` tag, for a clean uninstall. |

Settings that change tags (for example turning tags on later) take effect at the next check of an item; run the hard refresh to apply them immediately.

## API (admin only, `/WhereToWatch/...`)

- `GET config` effective settings, TMDB key redacted
- `GET tmdb/providers` flatrate providers TMDB lists for the primary country (optional `?country=`)
- `GET items/{id}` how one item was resolved: stream tags, recorded state, chosen badges
- `GET state` counts by status and latest run

## Build

Requires the .NET 10 SDK.

```
dotnet build Jellyfin.Plugin.WhereToWatch
dotnet test Jellyfin.Plugin.WhereToWatch.Tests
```

Copy `Jellyfin.Plugin.WhereToWatch.dll` into `<jellyfin-data>/plugins/Where to Watch_<version>/` and restart Jellyfin.

## Status

The pure logic (country fallback, tag diff, badge layout and rendering, state store) is covered by tests. The Jellyfin integration (middleware, scheduled tasks, controller, configuration page) compiles against Jellyfin 12 but still needs to be verified on a running server, in particular that the image middleware intercepts poster requests.

Not yet implemented: SVG logos, tags on Episodes.

## License

GPL-3.0, see `LICENSE`.
