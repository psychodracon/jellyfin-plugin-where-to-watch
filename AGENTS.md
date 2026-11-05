# jellyfin-plugin-where-to-watch

Jellyfin plugin that tags items and draws poster badges with their streaming services. Installation,
settings and behaviour are in [README.md](README.md), which is written for users.

- `dotnet build WhereToWatch.slnx` and `dotnet test WhereToWatch.slnx`. The plugin is in
  `Jellyfin.Plugin.WhereToWatch/` (scanning in `Scanning/`, TMDB access in `Tmdb/`, poster rendering in
  `Rendering/`, the details-page row in `Web/`), the tests in `Jellyfin.Plugin.WhereToWatch.Tests/`.
- The version has four parts and lives in `Directory.Build.props`; CI bumps the last part by default. CI also
  adds the release to `manifest.json` (`scripts/update-manifest.js`), so do not edit `versions` by hand.
- Keep the README in step with user-visible changes, including the settings table.
- The plugin catalog URL points at `manifest.json` on `main`, so the repository must be public for others to
  install it.
