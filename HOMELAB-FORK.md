# Homelab fork notes

Thin tracking fork of https://github.com/DaveBinM/Lidarr.Plugin.Qobuz.

## Local-only changes (never PR'd)
- `.github/workflows/build.yml`: `PLUGIN_VERSION` = `10.9.0.<run#>` (our release line).
- This file.

## Patch stack we upstream (PR to DaveBinM)
- `fix/apostrophe-search`: strip apostrophe glyphs from the Qobuz search query.

## Sync with upstream (DaveBinM)
    git fetch upstream
    git rebase upstream/main        # our thin stack replays on top
    git push --force-with-lease origin main
Merged-upstream patches drop out of the stack automatically. CI cuts a draft
release on push to main; publish it, and Lidarr auto-updates.

## Rebuild-on-Lidarr-update
Plugin references Lidarr.Core >= 3.0.0.4855 (floor), so it keeps loading across
host Lidarr updates. Only rebuild if the host drops below the floor or an API
the plugin uses changes (rare) — done automatically by CI when the ext/Lidarr
submodule is bumped upstream.

## Rollback
Reinstall DaveBinM's URL in Lidarr, or redeploy the backed-up original DLL at
lidarr:/config/qobuz-plugin-backup-20260704/.
