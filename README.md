# Where to Watch

[![CI](https://github.com/psychodracon/jellyfin-plugin-where-to-watch/actions/workflows/ci.yaml/badge.svg)](https://github.com/psychodracon/jellyfin-plugin-where-to-watch/actions/workflows/ci.yaml)

A [Jellyfin](https://jellyfin.org/) plugin that shows **where your movies and series are streaming**. It
looks up the streaming services for each title on [TMDB](https://www.themoviedb.org/) and shows them in
your library: as tags you can filter by, as logos on the posters, and as a row of links on each title's
page.

Only subscription ("flatrate") offers count; rent and buy offers are ignored.

## Features

### Tags

Every movie and series on one of your selected services gets a tag `stream:<name>`, such as
`stream:Netflix`, so you can filter your library by service.

- Episodes carry their series' tags.
- Tags follow the services: when a title leaves a service, its tag is removed on the next scan.
- Only `stream:*` tags are ever added or removed; your own tags are never touched.

### Poster badges

Posters of movies and series show the logos of the services they are on, up to three, in your priority
order, stacked from the corner you choose.

- **Your posters stay as they are.** Badges are drawn as posters are served, so turning the plugin off
  brings the originals straight back.
- **Placement.** The web client shows every poster in a fixed 2:3 card, centred and cropped. Badges are
  placed relative to that visible area, so the margin is the same on every poster even when its shape
  differs.
- **Format.** With `Auto`, the badged poster keeps the format Jellyfin served for that request (JPEG,
  PNG or WebP). Re-encoding always costs some quality: choose PNG, or WebP at quality 100 (lossless),
  for none, at the price of bigger files and a slower first render.
- **Caching.** Rendered posters are cached in Jellyfin's cache folder (`wheretowatch`) and pruned to
  512 MB after each scan. Changing a badge setting re-renders them automatically.

### "Watch on" row

A movie or series details page in the web UI gets a **Watch on** row under the overview with the same
logos. A click opens the title's TMDB watch page for your country, which links on to the service.

- The plugin adds a small script to Jellyfin's `index.html` as it is served; nothing on disk changes.
  It only affects the web UI, not the phone, TV or other apps.
- The row carries a small **JustWatch** credit, which TMDB requires for this data.

### Direct provider links

Optional, off by default: each logo in the **Watch on** row goes straight to the title at the service
instead of to TMDB. TMDB's API has no such links, but TMDB's watch page does, and the plugin reads them
from it.

- The scan fetches the page once per item that is on one of your selected services, one request at a
  time with a pause between them. Opening a details page fetches any links still missing.
- Links are refreshed with the rest of the provider data (default every 7 days), and by a hard refresh.
  A failed or empty result is retried after an hour, and the logo falls back to the TMDB page meanwhile.
- **Know what you are enabling.** This reads a website rather than an API. TMDB's terms restrict
  automated reading of its site (there is a carve-out for personal, non-commercial use that I would not
  rely on), TMDB could throttle or block your server's IP, and it stops working if TMDB changes that
  page. The links go through `click.justwatch.com`, as they do on TMDB's own site. It is **off by
  default** for these reasons.

## Requirements

- **Jellyfin 12.**
- A free **TMDB API key**: create an account on TMDB, then request a key under **Settings → API**.

The plugin is tested with the Jellyfin web UI. Tags are ordinary Jellyfin tags, and badges are drawn on
the poster images the server sends, so other clients should show them too, but that is not tested.

## Installation

### From the plugin repository (recommended)

1. In Jellyfin go to **Dashboard → Plugins → Repositories → Add**.
2. Name it `Where to Watch` and use this URL:
   `https://raw.githubusercontent.com/psychodracon/jellyfin-plugin-where-to-watch/main/manifest.json`
3. Open **Catalog**, find **Where to Watch** and install the latest version.
4. **Restart Jellyfin.** Plugins are only loaded at startup.

### Manually

1. Download `where-to-watch_<version>.zip` from the
   [latest release](https://github.com/psychodracon/jellyfin-plugin-where-to-watch/releases/latest) and
   unpack it, or build the plugin (see [Development](#development)).
2. Put `Jellyfin.Plugin.WhereToWatch.dll` in `<jellyfin-data>/plugins/Where to Watch_<version>/` (for
   example `Where to Watch_1.0.0.1`).
3. **Delete any older `Where to Watch_*` folder.** If two exist, Jellyfin loads the highest version.
4. **Restart Jellyfin.** Plugins are only loaded at startup.
5. Check **Dashboard → Plugins**: "Where to Watch" should be listed with the version you installed.

### First-time setup

1. **Dashboard → Plugins → Where to Watch.**
2. Paste your **TMDB API key** (a v3 key or a v4 read access token) and save.
3. Set your **countries**, primary first, for example `PL,US`. The first country in which one of your
   selected providers has the title is used.
4. Reload the page and click **Load providers from TMDB**. Tick the services you subscribe to. The order
   of the ticked ones is the badge priority (default: TMDB's own popularity order); use the arrows to
   change it. Save.
5. **Dashboard → Scheduled Tasks → Where to Watch: scan providers → run.** The first scan queries TMDB
   for every movie and series, so give it time. It can be stopped and resumes where it left off.
6. Open the library, with your browser cache disabled for the first look (see
   [Troubleshooting](#troubleshooting)).

Only items with a TMDB id are looked up.

## Usage

- **Filter by service:** in a library, open **Filter → Tags** and pick a `stream:` tag to see
  everything you can stream there.
- **Go to a service:** open a title and click a logo in the **Watch on** row.

The scan runs every night. The other tasks are under **Dashboard → Scheduled Tasks** for when you need
them:

| Task | Runs | What it does |
| --- | --- | --- |
| Where to Watch: scan providers | daily at 03:00 | Looks up new items and items past the refresh interval, tags them, and (if enabled) fetches direct links. Resumes after an interruption. |
| Where to Watch: hard refresh | manual | Clears rendered posters and re-checks every item, ignoring the refresh interval. Does not remove tags. |
| Where to Watch: remove all stream: tags | manual | Removes every `stream:` tag and marks all items due, so the next scan tags them again. Run it before uninstalling. |

Failed lookups are retried on the next run.

## Configuration

### Settings

All on **Dashboard → Plugins → Where to Watch**.

| Setting | Default | What it does |
| --- | --- | --- |
| TMDB API key | | Your v3 key or v4 token. Kept on the server and shown masked. |
| Countries | `PL,US` | Primary first. The first country where a selected service has the title wins. |
| Days before an item is checked again | 7 | Refresh interval for provider data and direct links. |
| Add `stream:` tags | on | Tagging. |
| Draw badges on posters | on | Poster badges. |
| Show a "Watch on" row | on | The details-page row. |
| Direct links | off | See [Direct provider links](#direct-provider-links). |
| Maximum badges per poster | 3 | |
| Corner | Bottom left | Where the first badge goes; more stack away from the edge. |
| Badge width | 25 | % of the visible poster width. |
| Badge width / height | 1 | 1 is square, suited to TMDB's square icons. |
| Margin | 2 | % of the visible poster width, from the card edges. |
| Gap between badges | 2 | % of the visible poster width. |
| Backdrop colour | `#99000000` | `#AARRGGBB` (here 60% black). |
| Backdrop corner radius | 8 | % of the badge height. |
| Space around the logo | 1 | % of the badge height. |
| Logo corner radius | 8 | % of the logo size; 50 makes a circle. |
| Poster format | Auto | Auto, JPEG, WebP or PNG. |
| Quality | 95 | JPEG and lossy WebP quality; 100 makes WebP lossless. |

Changing a badge setting needs no rescan. Changing the countries or selected providers triggers a full
re-check on the next scan.

### Custom logos

The badge and row use TMDB's provider icons, downloaded automatically (the first scan fetches them). To
use your own artwork, put a file named after the **TMDB provider id** in the plugin's `logos` folder:

```text
<plugin data folder>/logos/8.png        (Netflix)
<plugin data folder>/logos/1899.webp    (HBO Max)
```

`.png`, `.webp`, `.jpg` and `.jpeg` are accepted, and your file always wins over the downloaded icon.
Provider ids are shown next to each provider on the config page. The downloaded icons live in
`logos/cache/`. A larger, bold, square or rounded image works best. If you cannot find the plugin's data
folder, search your Jellyfin data directory for the `state.json` file or the `logos` folder created by
the first scan.

## Troubleshooting

**No badges, or the old posters still show.**
Check, in order:

1. Jellyfin was **restarted** after installing the DLL, and Dashboard → Plugins shows the version you
   expect.
2. The **scan has run**. In the browser console on the web UI, run
   `await ApiClient.getJSON(ApiClient.getUrl('WhereToWatch/state'))`: it shows the latest run, its
   progress and the number of items per status. Scanned items are `Resolved`.
3. Your browser is not showing a cached poster: reload with the cache disabled (DevTools → Network →
   "Disable cache"). Jellyfin gives posters a long cache lifetime.
4. In DevTools → Network, open a poster request (`Images/Primary`) and look for the response header
   `X-WhereToWatch: <version>; card-aspect=0.667`. Missing means the plugin did not draw on that image.
5. Jellyfin's log: a line `Where to Watch: no logo for provider …` means that service's logo is missing;
   run the scan (it downloads TMDB icons) or add your own file.

**No "Watch on" row.**
In the console run `document.querySelector('script[data-wheretowatch]')`. `null` means the script was not
added to the page: the plugin is not loaded, the row is switched off, or something in front of Jellyfin
(a proxy that caches `index.html`) is serving a stale copy. The row also needs the item to have been
scanned.

**The logos in the row are broken images.**
Open `https://<your-server>/WhereToWatch/logo/<providerId>` directly. If a reverse proxy answers with its
own error page, it is blocking anonymous requests to that path.

**Direct links keep falling back to the TMDB page.**
Look in Jellyfin's log for `TMDB watch page request returned <status>`: TMDB refused the request.
Otherwise TMDB may have changed its page.

**One provider falls back to the TMDB page while the others link directly.**
The plugin matches TMDB's provider to the page's link by id, then by name ("Disney+" and "Disney Plus"
count as the same), because the page does not always use TMDB's ids (Disney+ in Poland does not). If a
service still falls back, its name differs on the two sides; please report it.

**One title shows the wrong services.**
As an administrator, run `await ApiClient.getJSON(ApiClient.getUrl('WhereToWatch/items/<id>'))` in the
browser console, with the `id` from the title's details page address. It shows the title's tags, the
badges chosen, and what TMDB reports right now for each of your countries.

**Behind a reverse proxy.** The plugin deliberately uses no separate `.js` file and no file extensions in
its URLs, because some proxies block or reroute those.

## Limitations

- The "Watch on" row exists in the **web UI only**.
- SVG logos are not supported (PNG, WebP and JPEG are).
- Only **subscription** availability is shown, never rent or buy.
- Availability comes from TMDB (powered by JustWatch) and can lag or be wrong.
- Posters are placed for the web UI's 2:3 cards. Other clients may crop posters differently.

## Development

Requires the **.NET 10 SDK**.

```sh
dotnet build WhereToWatch.slnx
dotnet test WhereToWatch.slnx
dotnet publish Jellyfin.Plugin.WhereToWatch -c Release -o artifacts/publish
```

The result is `artifacts/publish/Jellyfin.Plugin.WhereToWatch.dll`. Jellyfin and SkiaSharp are supplied
by the server and are not bundled.

## License

Streaming availability data is provided by **TMDB**, powered by **JustWatch**. This product uses the TMDB
API but is not endorsed or certified by TMDB.

Copyright (C) Kamil Ganczarek. Licensed under the GNU General Public License, version 3 or (at your
option) any later version. See [LICENSE](LICENSE).
