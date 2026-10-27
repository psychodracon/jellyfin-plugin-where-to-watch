# Where to Watch

A Jellyfin plugin that shows **where your movies and series are streaming**. It looks up the streaming services for each title on TMDB, then:

- **tags** the item (`stream:Netflix`), so you can filter your library by service,
- **draws the service logo on the poster** (up to three, bottom-left by default),
- adds a **"Watch on"** row to the details page in the web UI.

Your stored posters are never modified. Badges are drawn as posters are served, so turning the plugin off brings the originals straight back.

> Requires **Jellyfin 12** and a free **TMDB API key**. Tested with the web UI. Tags are ordinary Jellyfin tags, and badges are drawn on the poster images the server sends, so other clients should show them too, but I have not tested that.

## Install

1. Build the plugin (see [Build](#build)) or take `Jellyfin.Plugin.WhereToWatch.dll` from a build.
2. Put the DLL in `<jellyfin-data>/plugins/Where to Watch_<version>/` (for example `Where to Watch_1.2.1.0`).
3. **Delete any older `Where to Watch_*` folder.** If two exist, Jellyfin loads the highest version.
4. **Restart Jellyfin.** Plugins are only loaded at startup.
5. Check **Dashboard → Plugins**: "Where to Watch" should be listed with the version you installed.

## First-time setup

1. **Dashboard → Plugins → Where to Watch.**
2. Paste your **TMDB API key** (a v3 key or a v4 read access token) and save.
3. Set your **countries**, primary first, for example `PL,US`. The first country in which one of your selected providers has the title is used.
4. Reload the page and click **Load providers from TMDB**. Tick the services you subscribe to. The order of the ticked ones is the badge priority (default: TMDB's own popularity order); use the arrows to change it. Save.
5. **Dashboard → Scheduled Tasks → Where to Watch: scan providers → run.** The first scan queries TMDB for every movie and series, so give it time. It can be stopped and resumes where it left off.
6. Open the library, with your browser cache disabled for the first look (see [Troubleshooting](#troubleshooting)).

Only items with a TMDB id are looked up, and only **subscription ("flatrate")** availability counts. Rent and buy offers are ignored.

## What it does

### Tags

Items on a selected service get a tag `stream:<name>`, for example `stream:Netflix`. Movies and series are tagged, and episodes carry their series' tags. Only `stream:*` tags are ever added or removed; your own tags are never touched. A tag is removed again when the title leaves that service.

### Poster badges

A dark, rounded tile with the service logo is drawn on the **Primary** image (the poster) of movies and series, stacked up from the corner you choose, in your provider priority order, up to the maximum you set (default 3).

- **Placement.** The web client shows every poster in a fixed 2:3 card, centred and cropped. Badges are placed relative to that visible area, so the margin is the same on every poster even when its shape differs.
- **Format.** By default (`Auto`) the badged poster keeps the format Jellyfin served for that request (JPEG, PNG or WebP). You can force JPEG, WebP or PNG. Re-encoding always costs some quality: choose PNG, or WebP at quality 100 (lossless), for none at the price of bigger files and a slower first render.
- **Caching.** Rendered posters are cached in Jellyfin's cache folder (`wheretowatch`) and pruned to 512 MB after each scan. Changing any badge setting re-renders them automatically.

### "Watch on" row (web UI only)

On a movie or series details page, a **Watch on** row (Polish UI: **Oglądaj na**) appears under the overview, with the logos of the services the title is on. It follows Jellyfin's display language (Polish, otherwise English) and carries a small **JustWatch** credit, which TMDB requires for this data.

- By default every logo opens the **TMDB watch page** for that title and country. That is the only link TMDB's API offers.
- **Optional direct links** (off by default): see [Direct provider links](#direct-provider-links-optional).
- The plugin adds a small script to Jellyfin's `index.html` as it is served (nothing on disk changes). It only affects the web UI, not the phone, TV or other apps. Turn the row off with **Show a "Watch on" row** on the config page.

#### Direct provider links (optional)

TMDB's API has no per-provider links, but its public watch page does (each logo there goes straight to the title at that service). With **Link each "Watch on" logo straight to the provider** enabled, the plugin reads those links from TMDB's page.

- The scan fetches the page once per item that is on one of your selected services, one request at a time with a pause between them, and keeps every stream provider's link from it. Opening a details page fetches any that are still missing.
- Links are refreshed with the rest of the provider data (default every 7 days). A hard refresh refetches them. A failed or empty result is retried after an hour.
- Any failure falls back to the TMDB page link.
- **Know what you are enabling.** This reads a website rather than an API. TMDB's terms restrict automated reading of its site (there is a carve-out for personal, non-commercial use that I would not rely on), TMDB could throttle or block your server's IP, and it stops working if TMDB changes that page. The links go through `click.justwatch.com`, as they do on TMDB's own site. It is **off by default** for these reasons.

## Settings

All on **Dashboard → Plugins → Where to Watch**.

| Setting | Default | What it does |
| --- | --- | --- |
| TMDB API key | | Your v3 key or v4 token. Kept on the server; the API shows it masked. |
| Countries | `PL,US` | Primary first. The first country where a selected service has the title wins. |
| Days before an item is checked again | 7 | Refresh interval for provider data and direct links. |
| Add `stream:` tags | on | Tagging. |
| Draw badges on posters | on | Poster badges. |
| Show a "Watch on" row | on | The details-page row. |
| Direct links | off | See above. |
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

Changing a badge setting needs no rescan. Changing the countries or selected providers triggers a full re-check on the next scan.

## Custom logos

The badge and row use TMDB's provider icons, downloaded automatically (the first scan fetches them). To use your own artwork, put a file named after the **TMDB provider id** in the plugin's `logos` folder:

```
<plugin data folder>/logos/8.png        (Netflix)
<plugin data folder>/logos/1899.webp    (HBO Max)
```

`.png`, `.webp`, `.jpg` and `.jpeg` are accepted, and your file always wins over the downloaded icon. Provider ids are shown next to each provider on the config page. The downloaded icons live in `logos/cache/`. A larger, bold, square or rounded image works best. If you cannot find the plugin's data folder, search your Jellyfin data directory for the `state.json` file or the `logos` folder created by the first scan.

## Scheduled tasks

| Task | Runs | What it does |
| --- | --- | --- |
| Where to Watch: scan providers | daily at 03:00 | Looks up new items and items past the refresh interval, tags them, and (if enabled) fetches direct links. Resumes after an interruption. |
| Where to Watch: hard refresh | manual | Clears rendered posters and re-checks every item, ignoring the refresh interval. Does not remove tags. |
| Where to Watch: remove all stream: tags | manual | Removes every `stream:` tag and marks all items due, so the next scan tags them again. Use before uninstalling. |

Progress is stored in `state.json` in the plugin's data folder (written atomically and checkpointed every ~30 s or 200 items). Failed lookups are retried on the next run.

## API

All under `/WhereToWatch/`. The first group is **admin only**.

| Endpoint | Returns |
| --- | --- |
| `GET config` | Effective settings, TMDB key masked. |
| `GET tmdb/providers` | The subscription services TMDB lists for your primary country (optional `?country=`). Used by the config page. |
| `GET items/{id}` | How one item was resolved: stream tags, stored state, chosen badges, and what TMDB reports right now for each configured country. |
| `GET state` | Counts by status, the latest run and its progress. |

Used by the "Watch on" row:

| Endpoint | Access | Returns |
| --- | --- | --- |
| `GET watch/{itemId}` | signed-in user | The services, country and link(s) to show for an item. |
| `GET logo/{providerId}` | anonymous | A service logo. It is loaded through an image tag, which cannot send credentials. The URL has no file extension on purpose. |

In the web UI console, `await ApiClient.getJSON(ApiClient.getUrl('WhereToWatch/state'))` shows scan progress using your signed-in session.

## Troubleshooting

**No badges, or the old posters still show.**
Check, in order:
1. Jellyfin was **restarted** after installing the DLL, and Dashboard → Plugins shows the version you expect.
2. The **scan has run**: `WhereToWatch/state` should show items with status `Resolved`.
3. Your browser is not showing a cached poster: reload with the cache disabled (DevTools → Network → "Disable cache"). Jellyfin gives posters a long cache lifetime.
4. In DevTools → Network, open a poster request (`Images/Primary`) and look for the response header `X-WhereToWatch: <version>; card-aspect=0.667`. Missing means the plugin did not draw on that image.
5. Jellyfin's log: a line `Where to Watch: no logo for provider …` means that service's logo is missing; run the scan (it downloads TMDB icons) or add your own file.

**No "Watch on" row.**
In the console run `document.querySelector('script[data-wheretowatch]')`. `null` means the script was not added to the page: the plugin is not loaded, the row is switched off, or something in front of Jellyfin (a proxy that caches `index.html`) is serving a stale copy. The row also needs the item to have been scanned.

**The logos in the row are broken images.**
Open `https://<your-server>/WhereToWatch/logo/<providerId>` directly. If a reverse proxy answers with its own error page, it is blocking anonymous requests to that path.

**Direct links keep falling back to the TMDB page.**
Look in Jellyfin's log for `TMDB watch page request returned <status>`: TMDB refused the request. Otherwise TMDB may have changed its page.

**Behind a reverse proxy.** The plugin deliberately uses no separate `.js` file and no file extensions in its URLs, because some proxies block or reroute those.

## Limitations

- The "Watch on" row exists in the **web UI only**.
- SVG logos are not supported (PNG, WebP and JPEG are).
- Only **subscription** availability is shown, never rent or buy.
- Availability comes from TMDB (powered by JustWatch) and can lag or be wrong.
- Posters are placed for the web UI's 2:3 cards. Other clients may crop posters differently.

## Build

Requires the **.NET 10 SDK**.

```
dotnet build WhereToWatch.slnx
dotnet test WhereToWatch.slnx
dotnet publish Jellyfin.Plugin.WhereToWatch -c Release -o artifacts/publish
```

The result is `artifacts/publish/Jellyfin.Plugin.WhereToWatch.dll`. Jellyfin and SkiaSharp are supplied by the server and are not bundled.

The unit tests cover the logic that does not need a running server: country fallback, tag changes, badge layout and rendering, the state file, the HTML injection and the TMDB page parser. The poster badges have been checked on a running Jellyfin 12. The "Watch on" row and the direct links are tested at the logic level only and still need checking against your own server.

## Credits and license

Streaming availability data is provided by **TMDB**, powered by **JustWatch**. This product uses the TMDB API but is not endorsed or certified by TMDB.

GPL-3.0. See `LICENSE`.
