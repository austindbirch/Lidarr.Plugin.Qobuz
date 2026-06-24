using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Processes;

namespace NzbDrone.Core.Download.Clients.Qobuz
{
    // Post-completion handling for a successfully downloaded album: move it into
    // <CompletedDownloadDirectory>/Artist/Album and optionally run a user script. All failures
    // here are non-fatal to the download itself (see MoveCompletedAlbum / RunScript).
    public class CompletedDownloadHandler
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IProcessProvider _processProvider;
        private readonly Logger _logger;

        public CompletedDownloadHandler(IDiskProvider diskProvider, IDiskTransferService diskTransferService, IProcessProvider processProvider, Logger logger)
        {
            _diskProvider = diskProvider;
            _diskTransferService = diskTransferService;
            _processProvider = processProvider;
            _logger = logger;
        }

        // Returns the directory Lidarr should import from: the final destination on a successful
        // move, or the unchanged source directory when the feature is disabled or the move fails
        // (in which case the original files are left in place and the script is not run).
        public string MoveCompletedAlbum(string sourceAlbumDir, string qobuzAlbumId, string quality, int trackCount, LidarrInfo lidarr, QobuzSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.CompletedDownloadDirectory))
                return sourceAlbumDir;

            if (string.IsNullOrWhiteSpace(sourceAlbumDir) || !_diskProvider.FolderExists(sourceAlbumDir))
            {
                _logger.Warn("Qobuz completed-download move skipped: source folder '{0}' is missing.", sourceAlbumDir);
                return sourceAlbumDir;
            }

            var trimmedSource = sourceAlbumDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var album = Path.GetFileName(trimmedSource);
            var artist = Path.GetFileName(Path.GetDirectoryName(trimmedSource));
            var artistDir = Path.Combine(settings.CompletedDownloadDirectory, artist);
            var finalDir = Path.Combine(artistDir, album);

            if (PathsEqual(finalDir, sourceAlbumDir))
            {
                // Already in place (e.g. completed dir resolves to the download path); run the script only.
                RunScript(finalDir, qobuzAlbumId, artist, album, quality, trackCount, lidarr, settings);
                return finalDir;
            }

            try
            {
                if (_diskProvider.FolderExists(finalDir))
                {
                    _logger.Warn("Qobuz completed-download destination '{0}' already exists; overwriting.", finalDir);
                    _diskProvider.DeleteFolder(finalDir, true);
                }

                if (!_diskProvider.FolderExists(artistDir))
                    _diskProvider.CreateFolder(artistDir);

                _diskTransferService.TransferFolder(sourceAlbumDir, finalDir, TransferMode.Move);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Qobuz completed-download move failed for '{0}' -> '{1}'; leaving files in place and skipping the script.", sourceAlbumDir, finalDir);
                return sourceAlbumDir;
            }

            _logger.Info("Qobuz moved completed album to '{0}'.", finalDir);
            RunScript(finalDir, qobuzAlbumId, artist, album, quality, trackCount, lidarr, settings);
            return finalDir;
        }

        private void RunScript(string albumDir, string qobuzAlbumId, string artist, string album, string quality, int trackCount, LidarrInfo lidarr, QobuzSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.PostDownloadScript))
                return;

            try
            {
                var args = string.IsNullOrWhiteSpace(qobuzAlbumId)
                    ? $"\"{albumDir}\""
                    : $"\"{albumDir}\" \"{qobuzAlbumId}\"";

                var environment = new StringDictionary
                {
                    { "Qobuz_AlbumDir", albumDir },
                    { "Qobuz_AlbumId", qobuzAlbumId ?? string.Empty },
                    { "Qobuz_Artist", artist ?? string.Empty },
                    { "Qobuz_Album", album ?? string.Empty },
                    { "Qobuz_Quality", quality ?? string.Empty },
                    { "Qobuz_TrackCount", trackCount.ToString() },
                    { "Lidarr_AlbumId", lidarr.AlbumIds ?? string.Empty },
                    { "Lidarr_ReleaseGuid", lidarr.ReleaseGuid ?? string.Empty },
                    { "Lidarr_ReleaseTitle", lidarr.ReleaseTitle ?? string.Empty },
                };

                var stdout = new List<string>();
                var stderr = new List<string>();

                _logger.Debug("Running Qobuz post-download script: {0} {1}", settings.PostDownloadScript, args);

                var process = _processProvider.Start(
                    settings.PostDownloadScript,
                    args,
                    environment,
                    s => { lock (stdout) { stdout.Add(s); } },
                    s => { lock (stderr) { stderr.Add(s); } });

                if (settings.PostDownloadScriptTimeout > 0)
                {
                    if (!process.WaitForExit(settings.PostDownloadScriptTimeout * 1000))
                    {
                        _logger.Error("Qobuz post-download script timed out after {0}s and was killed: {1}", settings.PostDownloadScriptTimeout, settings.PostDownloadScript);
                        TryKill(process);
                        return;
                    }
                }
                else
                {
                    process.WaitForExit();
                }

                string Join(List<string> lines)
                {
                    lock (lines) { return string.Join(Environment.NewLine, lines); }
                }

                if (process.ExitCode != 0)
                {
                    _logger.Error("Qobuz post-download script exited with code {0} for '{1}'.{2}stdout: {3}{2}stderr: {4}",
                        process.ExitCode, albumDir, Environment.NewLine, Join(stdout), Join(stderr));
                }
                else
                {
                    _logger.Debug("Qobuz post-download script completed successfully (exit 0) for '{0}'.", albumDir);
                }
            }
            catch (Exception ex)
            {
                // A script that won't start / can't be killed must never fail the album download.
                _logger.Error(ex, "Qobuz post-download script failed to run: {0}", settings.PostDownloadScript);
            }
        }

        private void TryKill(Process process)
        {
            try
            {
                _processProvider.Kill(process.Id);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to kill timed-out Qobuz post-download script.");
            }
        }

        private static bool PathsEqual(string a, string b)
        {
            var sa = Path.GetFullPath(a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var sb = Path.GetFullPath(b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Lidarr identifiers handed to the post-download script so it can act on the album
    // (e.g. unmonitor it). AlbumIds is comma-joined; usually a single id.
    public record LidarrInfo(string AlbumIds, string ReleaseGuid, string ReleaseTitle);
}
