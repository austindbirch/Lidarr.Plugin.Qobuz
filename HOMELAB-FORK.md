# Homelab fork notes

Thin tracking fork of https://github.com/DaveBinM/Lidarr.Plugin.Qobuz.

## Local-only changes (never PR'd)
- `.github/workflows/build.yml`: `PLUGIN_VERSION` = `10.9.0.<run#>` (our release line)
  and `draft: false` (auto-publish).
- `src/Lidarr.Plugin.Qobuz/Plugin.cs`: `Owner`/`GithubUrl` point at our fork so
  Lidarr tracks us for updates.
- This file.

## Patch stack we upstream (PR to DaveBinM)
- `feat/search-relaxation-tiers-upstream`: extracts query logic into
  `QobuzSearchQuery` (with unit tests + isolated `test.yml` CI), adds apostrophe
  normalization, and adds relaxed + artist-only fallback search tiers. This
  supersedes the older `fix/apostrophe-search` branch (apostrophe fix is folded in).

## Sync with upstream (DaveBinM)
    git fetch upstream
    git rebase upstream/main        # our thin stack replays on top
    git push --force-with-lease origin main
Merged-upstream patches drop out of the stack automatically. CI auto-publishes a
release on push to main (draft: false); Lidarr auto-updates.

## Rebuild-on-Lidarr-update
Plugin references Lidarr.Core >= 3.0.0.4855 (floor), so it keeps loading across
host Lidarr updates. Only rebuild if the host drops below the floor or an API
the plugin uses changes (rare) — done automatically by CI when the ext/Lidarr
submodule is bumped upstream.

## Rollback
Reinstall DaveBinM's URL in Lidarr, or redeploy the backed-up original DLL at
lidarr:/config/qobuz-plugin-backup-20260704/.
