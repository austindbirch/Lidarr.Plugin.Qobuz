using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;
using NzbDrone.Core.Download.Clients.Qobuz.Queue;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Plugin.Qobuz.API;

namespace NzbDrone.Core.Download.Clients.Qobuz
{
    public interface IQobuzProxy
    {
        List<DownloadClientItem> GetQueue(QobuzSettings settings);
        Task<string> Download(RemoteAlbum remoteAlbum, QobuzSettings settings);
        void RemoveFromQueue(string downloadId, QobuzSettings settings);
    }

    public class QobuzProxy : IQobuzProxy
    {
        private readonly ICached<DateTime?> _startTimeCache;
        private readonly DownloadTaskQueue _taskQueue;

        public QobuzProxy(ICacheManager cacheManager,
                          IDiskProvider diskProvider,
                          IDiskTransferService diskTransferService,
                          IProcessProvider processProvider,
                          Logger logger)
        {
            _startTimeCache = cacheManager.GetCache<DateTime?>(GetType(), "startTimes");
            var completedHandler = new CompletedDownloadHandler(diskProvider, diskTransferService, processProvider, logger);
            _taskQueue = new(500, null, logger, completedHandler);

            _taskQueue.StartQueueHandler();
        }

        public List<DownloadClientItem> GetQueue(QobuzSettings settings)
        {
            _taskQueue.SetSettings(settings);

            var listing = _taskQueue.GetQueueListing();
            var completed = listing.Where(x => x.Status == DownloadItemStatus.Completed);
            var queue = listing.Where(x => x.Status == DownloadItemStatus.Queued);
            var current = listing.Where(x => x.Status == DownloadItemStatus.Downloading);

            var result = completed.Concat(current).Concat(queue).Where(x => x != null).Select(ToDownloadClientItem).ToList();

            return result;
        }

        public void RemoveFromQueue(string downloadId, QobuzSettings settings)
        {
            _taskQueue.SetSettings(settings);

            var item = _taskQueue.GetQueueListing().FirstOrDefault(a => a.ID == downloadId);
            if (item != null)
                _taskQueue.RemoveItem(item);
        }

        public async Task<string> Download(RemoteAlbum remoteAlbum, QobuzSettings settings)
        {
            _taskQueue.SetSettings(settings);

            var downloadItem = await DownloadItem.From(remoteAlbum);
            await _taskQueue.QueueBackgroundWorkItemAsync(downloadItem);
            return downloadItem.ID;
        }

        private DownloadClientItem ToDownloadClientItem(DownloadItem x)
        {
            var format = x.Bitrate switch
            {
                AudioQuality.MP3320 => "MP3 320kbps",
                AudioQuality.FLACLossless => "FLAC Lossless",
                AudioQuality.FLACHiRes24Bit96kHz => "FLAC 24bit 96kHz",
                AudioQuality.FLACHiRes24Bit192Khz => "FLAC 24bit 192kHz",
                _ => throw new NotImplementedException(),
            };

            var title = $"{x.Artist} - {x.Title} [WEB] [{format}]";
            if (x.Explicit)
            {
                title += " [Explicit]";
            }

            var item = new DownloadClientItem
            {
                DownloadId = x.ID,
                Title = title,
                TotalSize = x.TotalSize,
                RemainingSize = x.TotalSize - x.DownloadedSize,
                RemainingTime = GetRemainingTime(x),
                Status = x.Status,
                CanMoveFiles = true,
                CanBeRemoved = true,
            };

            if (x.DownloadFolder.IsNotNullOrWhiteSpace())
            {
                item.OutputPath = new OsPath(x.DownloadFolder);
            }

            return item;
        }

        private TimeSpan? GetRemainingTime(DownloadItem x)
        {
            if (x.Status == DownloadItemStatus.Completed)
            {
                _startTimeCache.Remove(x.ID);
                return null;
            }

            if (x.Progress == 0)
            {
                return null;
            }

            var started = _startTimeCache.Find(x.ID);
            if (started == null)
            {
                started = DateTime.UtcNow;
                _startTimeCache.Set(x.ID, started);
                return null;
            }

            var elapsed = DateTime.UtcNow - started;
            var progress = Math.Min(x.Progress, 1);

            return TimeSpan.FromTicks((long)(elapsed.Value.Ticks * (1 - progress) / progress));
        }
    }
}
