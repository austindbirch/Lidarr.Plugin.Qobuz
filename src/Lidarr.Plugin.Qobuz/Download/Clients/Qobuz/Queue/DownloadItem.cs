using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Plugins;
using NzbDrone.Plugin.Qobuz.API;
using QobuzApiSharp.Exceptions;
using QobuzApiSharp.Models.Content;

namespace NzbDrone.Core.Download.Clients.Qobuz.Queue
{
    public class DownloadItem
    {
        public static async Task<DownloadItem> From(RemoteAlbum remoteAlbum)
        {
            var url = remoteAlbum.Release.DownloadUrl.Trim();
            var quality = remoteAlbum.Release.Container switch
            {
                "320" => AudioQuality.MP3320,
                "Lossless" => AudioQuality.FLACLossless,
                "24bit 96kHz" => AudioQuality.FLACHiRes24Bit96kHz,
                "24bit 192kHz" => AudioQuality.FLACHiRes24Bit192Khz,
                _ => AudioQuality.MP3320,
            };

            DownloadItem item = null;
            if (url.Contains("qobuz", StringComparison.CurrentCultureIgnoreCase))
            {
                if (QobuzURL.TryParse(url, out var qobuzUrl))
                {
                    item = new()
                    {
                        ID = Guid.NewGuid().ToString(),
                        Status = DownloadItemStatus.Queued,
                        Bitrate = quality,
                        RemoteAlbum = remoteAlbum,
                        _qobuzUrl = qobuzUrl,
                    };

                    await item.SetQobuzData();
                }
            }

            return item;
        }

        public string ID { get; private set; }

        public string Title { get; private set; }
        public string Artist { get; private set; }
        public bool Explicit { get; private set; }

        public RemoteAlbum RemoteAlbum {  get; private set; }

        public string DownloadFolder { get; private set; }

        public AudioQuality Bitrate { get; private set; }
        public DownloadItemStatus Status { get; set; }

        public float Progress { get => DownloadedSize / (float)Math.Max(TotalSize, 1); }

        // Backed by fields and mutated with Interlocked because up to 3 track tasks update these
        // concurrently; a lost ++ here can wrongly mark an incomplete album as Completed.
        private long _downloadedSize;
        public long DownloadedSize => Interlocked.Read(ref _downloadedSize);
        public long TotalSize { get; private set; }

        private int _failedTracks;
        public int FailedTracks => _failedTracks;
        private int _skippedTracks;
        public int SkippedTracks => _skippedTracks;

        private Track[] _tracks;
        private QobuzURL _qobuzUrl;
        private Album _qobuzAlbum;

        public async Task DoDownload(QobuzSettings settings, Logger logger, CompletedDownloadHandler completedHandler, CancellationToken cancellation = default)
        {
            List<Task> tasks = new();
            using SemaphoreSlim semaphore = new(3, 3);
            foreach (var track in _tracks)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellation);
                    try
                    {
                        if (track.Streamable == false)
                        {
                            logger.Warn("Qobuz track {0} ({1}) is not streamable and will be skipped.", track.Id, track.Title);
                            Interlocked.Increment(ref _skippedTracks);
                            return;
                        }

                        // Pre-check: downgrade Hi-Res → lossless when track flags say Hi-Res isn't available
                        var startingBitrate = Bitrate;
                        if ((Bitrate == AudioQuality.FLACHiRes24Bit192Khz || Bitrate == AudioQuality.FLACHiRes24Bit96kHz)
                            && track.HiresStreamable == false)
                        {
                            logger.Info("Qobuz track {0} ({1}) is not Hi-Res streamable; falling back to FLAC lossless.", track.Id, track.Title);
                            startingBitrate = AudioQuality.FLACLossless;
                        }

                        // Quality upgrade chain: if a tier 404s, try the next one up before skipping
                        var qualityChain = GetQualityUpgradeChain(startingBitrate, track);
                        bool downloaded = false;
                        const int maxRetries = 3;

                        foreach (var quality in qualityChain)
                        {
                            if (downloaded) break;
                            if (quality != startingBitrate)
                                logger.Info("Qobuz track {0} ({1}): upgrading quality to {2}.", track.Id, track.Title, quality);

                            for (int attempt = 1; attempt <= maxRetries; attempt++)
                            {
                                try
                                {
                                    await DoTrackDownload(track.Id.ToString(), quality, settings, cancellation);
                                    if (quality != Bitrate)
                                        logger.Info("Qobuz track {0} ({1}): downloaded at quality {2} instead of requested {3}.", track.Id, track.Title, quality, Bitrate);
                                    Interlocked.Increment(ref _downloadedSize);
                                    downloaded = true;
                                    break;
                                }
                                catch (TaskCanceledException) when (cancellation.IsCancellationRequested)
                                {
                                    throw;
                                }
                                catch (ApiErrorResponseException ex) when (ex.ResponseStatusCode == "404")
                                {
                                    logger.Warn("Qobuz track {0} ({1}) returned 404 at quality {2}.", track.Id, track.Title, quality);
                                    break; // try next quality tier
                                }
                                catch (Exception ex) when (attempt < maxRetries)
                                {
                                    logger.Warn("Qobuz track {0} ({1}) failed (attempt {2}/{3}): {4}. Retrying...", track.Id, track.Title, attempt, maxRetries, ex.Message);
                                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellation);
                                }
                                catch (Exception ex)
                                {
                                    logger.Error("Qobuz track {0} ({1}) failed after {2} attempts: {3}", track.Id, track.Title, maxRetries, ex.Message);
                                    Interlocked.Increment(ref _failedTracks);
                                    downloaded = true; // stop trying further qualities
                                    break;
                                }
                            }
                        }

                        if (!downloaded)
                        {
                            logger.Warn("Qobuz track {0} ({1}) is not available at any quality and will be skipped.", track.Id, track.Title);
                            Interlocked.Increment(ref _skippedTracks);
                        }
                        return;
                    }
                    catch (TaskCanceledException) when (cancellation.IsCancellationRequested) { }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellation));
            }

            await Task.WhenAll(tasks);

            bool incomplete = FailedTracks > 0
                || DownloadedSize + SkippedTracks < _tracks.Length
                || (settings.RequireCompleteAlbum && SkippedTracks > 0);

            if (incomplete)
            {
                logger.Warn("Qobuz download incomplete: {0}/{1} tracks downloaded, {2} failed, {3} skipped.", DownloadedSize, _tracks.Length, FailedTracks, SkippedTracks);
                Status = DownloadItemStatus.Failed;
            }
            else
            {
                if (SkippedTracks > 0)
                    logger.Warn("Qobuz download completed with {0} skipped track(s) not available individually.", SkippedTracks);

                // Post-completion side effects only; failures here never flip the album to Failed.
                if (!string.IsNullOrWhiteSpace(DownloadFolder))
                {
                    var lidarr = new LidarrInfo(
                        string.Join(",", RemoteAlbum.Albums.Select(a => a.Id)),
                        RemoteAlbum.Release?.Guid ?? string.Empty,
                        RemoteAlbum.Release?.Title ?? string.Empty);
                    DownloadFolder = completedHandler.MoveCompletedAlbum(DownloadFolder, _qobuzUrl.Id, Bitrate.ToString(), _tracks.Length, lidarr, settings);
                }

                Status = DownloadItemStatus.Completed;
            }
        }

        private async Task DoTrackDownload(string track, AudioQuality bitrate, QobuzSettings settings, CancellationToken cancellation = default)
        {
            var page = QobuzAPI.Instance.Client.GetTrack(track, true);
            var songTitle = page.CompleteTitle;
            var artistName = page.Performer.Name;
            var albumTitle = page.Album.CompleteTitle;
            var duration = page.Duration;

            var ext = bitrate == AudioQuality.MP3320 ? "mp3" : "flac";
            var outPath = Path.Combine(settings.DownloadPath, MetadataUtilities.GetFilledTemplate("%albumartist%/%album%/", ext, page, _qobuzAlbum), MetadataUtilities.GetFilledTemplate("%volume% - %track% - %title%.%ext%", ext, page, _qobuzAlbum));
            var outDir = Path.GetDirectoryName(outPath)!;

            DownloadFolder = outDir;
            if (!Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            await QobuzAPI.Instance.Client.WriteRawTrackToFile(track, outPath, bitrate, cancellation);

            var fileSize = new FileInfo(outPath).Length;
            if (fileSize < 50_000)
            {
                File.Delete(outPath);
                throw new Exception($"Downloaded file for track {track} is too small ({fileSize} bytes); likely a failed or partial download.");
            }

            var plainLyrics = string.Empty;
            string syncLyrics = null;

            if (settings.UseLRCLIB && (string.IsNullOrWhiteSpace(plainLyrics) || (settings.SaveSyncedLyrics && !(syncLyrics?.Any() ?? false))))
            {
                var lyrics = await Downloader.FetchLyricsFromLRCLIB("lrclib.net", songTitle, artistName, albumTitle, duration ?? 0, cancellation);
                if (lyrics != null)
                {
                    if (string.IsNullOrWhiteSpace(plainLyrics))
                        plainLyrics = lyrics.Value.plainLyrics;
                    if (settings.SaveSyncedLyrics && !(syncLyrics?.Any() ?? false))
                        syncLyrics = lyrics.Value.syncLyrics;
                }
            }

            await QobuzAPI.Instance.Client.ApplyMetadataToFile(track, outPath, plainLyrics, token: cancellation);

            if (!string.IsNullOrWhiteSpace(syncLyrics))
                await CreateLrcFile(Path.Combine(outDir, MetadataUtilities.GetFilledTemplate("%volume% - %track% - %title%.%ext%", "lrc", page, _qobuzAlbum)), syncLyrics);

            // TODO: this is currently a waste of resources, if this pr ever gets merged, it can be reenabled
            // https://github.com/Lidarr/Lidarr/pull/4370
            /* try
            {
                string artOut = Path.Combine(outDir, "folder.jpg");
                if (!File.Exists(artOut))
                {
                    byte[] bigArt = await QobuzAPI.Instance.Client.Downloader.GetArtBytes(page["DATA"]!["ALB_PICTURE"]!.ToString(), 1024, cancellation);
                    await File.WriteAllBytesAsync(artOut, bigArt, cancellation);
                }
            }
            catch (UnavailableArtException) { } */
        }

        private async Task SetQobuzData(CancellationToken cancellation = default)
        {
            if (_qobuzUrl.EntityType != EntityType.Album)
                throw new InvalidOperationException();

            var album = QobuzAPI.Instance.Client.GetAlbum(_qobuzUrl.Id, true);
            _tracks ??= album.Tracks.Items.ToArray();

            _qobuzAlbum = album;

            Title = album.CompleteTitle;
            Artist = album.Artist.Name;
            Explicit = album.ParentalWarning.GetValueOrDefault();
            TotalSize = _tracks.Length;
        }

        private static async Task CreateLrcFile(string lrcFilePath, string syncLyrics)
        {
            await File.WriteAllTextAsync(lrcFilePath, syncLyrics);
        }

        // Returns the quality tiers to attempt in order. After the flag-based pre-check has
        // already downgraded Hi-Res → lossless where appropriate, this handles the remaining
        // cases: MP3 or lossless that 404s but a higher tier IS available.
        private static IEnumerable<AudioQuality> GetQualityUpgradeChain(AudioQuality startingQuality, Track track)
        {
            yield return startingQuality;

            switch (startingQuality)
            {
                case AudioQuality.MP3320:
                    yield return AudioQuality.FLACLossless;
                    if (track.HiresStreamable == true)
                    {
                        yield return AudioQuality.FLACHiRes24Bit96kHz;
                        yield return AudioQuality.FLACHiRes24Bit192Khz;
                    }
                    break;

                case AudioQuality.FLACLossless:
                    if (track.HiresStreamable == true)
                    {
                        yield return AudioQuality.FLACHiRes24Bit96kHz;
                        yield return AudioQuality.FLACHiRes24Bit192Khz;
                    }
                    break;

                case AudioQuality.FLACHiRes24Bit96kHz:
                    yield return AudioQuality.FLACHiRes24Bit192Khz;
                    break;

                case AudioQuality.FLACHiRes24Bit192Khz:
                    yield return AudioQuality.FLACHiRes24Bit96kHz;
                    break;
            }
        }
    }
}
