# jellyfin-plugin-where-to-watch

Jellyfin plugin that tags movies and series with their streaming services (from TMDB) and draws service
badges on posters. Installation, settings and behaviour are in [README.md](README.md), which is written
for users.

## Commands

```sh
dotnet build WhereToWatch.slnx
dotnet test WhereToWatch.slnx
```

## Conventions

- Assemblies the Jellyfin server ships (`Jellyfin.*`, SkiaSharp) are referenced compile-only
  (`ExcludeAssets="runtime"` or `IncludeAssets="compile"`) so the plugin loads the host's copy; add any
  new host-supplied reference the same way.

## Git and releases

- The version has four parts, in `Directory.Build.props`. CI bumps it and adds each release to
  `manifest.json`; edit neither by hand.
- The catalogue URL points at `manifest.json` on `main`, so the repository must stay public.

## Boundaries

- **Always:** keep the README settings table in step with configuration changes.
- **Ask first:** raising the Jellyfin package versions or `targetAbi` (drops older servers); changing tag
  names (existing libraries carry them).
- **Never:** write to stored poster images; badges are drawn on the fly.
