# Where to Watch

A Jellyfin 12 plugin that finds which streaming services carry your Movies and Series (via TMDB), tags them, and draws provider badges on posters, Kometa-style. Stored images are never modified: badges are drawn as posters are served.

## What it does

- **Tags** items with `stream:<provider>` (for example `stream:Netflix`) on Movies, Series and their Episodes (episodes carry their series' tags). Only `stream:*` tags are ever added or removed.
- **Badges** up to N provider logos stacked on the Primary poster of Movies and Series. Position, size, margin, spacing, backdrop colour, backdrop and logo corner radius, space around the logo, output format and quality are configurable. The defaults give a rounded dark tile around each logo, bottom-left.
- Uses **flatrate** (subscription) availability only, with an **ordered country fallback** (for example `PL`, then `US`): the first country where a selected provider has the title is used.
- **State and resume:** per-item results are stored in `<plugin data>/state.json`, written atomically and checkpointed during a run. An interrupted run resumes where it stopped. Items are re-checked after a configurable number of days; failed lookups are retried on the next run; changing the countries or selected providers triggers a full re-check.

## How badges are drawn

A small middleware intercepts `Items/{id}/Images/Primary` requests for items that have providers in the state, draws the badges on the poster Jellyfin would have served, and caches the result under Jellyfin's cache folder. Disabling the plugin or the badges brings the original posters straight back.

**Where badges go.** Poster cards are a fixed 2:3 box. The web client scales each poster to cover that box and centres it, so a poster of any other shape is cropped at the sides (or at the top and bottom). Badges are therefore placed, sized and inset by the margin relative to the centred 2:3 part of the poster that is actually visible. Otherwise the margin on the cropped sides would disappear. (The `fillWidth`/`fillHeight` in an image request is the poster's own shape, not the card's, so it is not used.)

**Poster format.** By default (`Auto`) the badged poster keeps the format Jellyfin served for that request (JPEG, PNG or WebP), so every client keeps getting a format it already accepts. JPEG is encoded without chroma subsampling (4:4:4) at quality 95 by default. Because the poster is decoded and encoded again, JPEG is never perfectly lossless; for the least loss pick `Png` (lossless but large) or `Webp` with quality 100 (lossless), provided your clients accept WebP.

Logo lookup order per provider id:

1. `<plugin data>/logos/<providerId>.png` (also `.webp`, `.jpg`, `.jpeg`)
2. the TMDB icon, downloaded to `<plugin data>/logos/cache/`

Provider ids are shown next to each provider on the configuration page.

## Scheduled tasks

| Task | Runs | What it does |
| --- | --- | --- |
| Where to Watch: scan providers | daily 03:00 | Checks new items and items past the refresh interval; applies tags. Resumes after an interruption. |
| Where to Watch: hard refresh | manual | Clears rendered posters and re-checks every item, ignoring the refresh interval. Does not remove tags. |
| Where to Watch: remove all stream: tags | manual | Removes every `stream:` tag and marks all items due, so the next scan tags them again. |

Rendered posters are cached under Jellyfin's cache folder and pruned to 512 MB after each scan.

Settings that change tags (for example turning tags on later) take effect at the next check of an item; run the hard refresh to apply them immediately.

## API (admin only, `/WhereToWatch/...`)

- `GET config` effective settings, TMDB key redacted
- `GET tmdb/providers` flatrate providers TMDB lists for the primary country (optional `?country=`)
- `GET items/{id}` how one item was resolved: stream tags, recorded state, chosen badges, and what TMDB reports right now for each configured country
- `GET state` counts by status, latest run and its progress

## Build

Requires the .NET 10 SDK.

```
dotnet build Jellyfin.Plugin.WhereToWatch
dotnet test Jellyfin.Plugin.WhereToWatch.Tests
```

Copy `Jellyfin.Plugin.WhereToWatch.dll` into `<jellyfin-data>/plugins/Where to Watch_<version>/` and restart Jellyfin.

## Status

The pure logic (country fallback, tag diff, badge layout and rendering, state store) is covered by tests. The Jellyfin integration (middleware, scheduled tasks, controller, configuration page) compiles against Jellyfin 12 but still needs to be verified on a running server, in particular that the image middleware intercepts poster requests.

Not yet implemented: SVG logos, cache pre-warming in the daily task, and the Jellyfin 12 spike with a fallback to baking badges into stored posters if the middleware does not intercept poster requests.

## License

GPL-3.0, see `LICENSE`.
