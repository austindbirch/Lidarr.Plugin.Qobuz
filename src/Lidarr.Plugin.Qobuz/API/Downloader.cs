using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using NzbDrone.Core.Parser;
using QobuzApiSharp.Service;
using QobuzApiSharp.Models.Content;

namespace NzbDrone.Plugin.Qobuz.API;

public static class Downloader
{
    // No global timeout: large Hi-Res FLAC tracks can take much longer than the default 100s
    // to download. Each download attempt is instead bounded by a linked token (see below) so a
    // genuinely stalled connection still fails and is retried, while a slow-but-progressing one
    // is allowed to finish.
    private static readonly HttpClient _client = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task WriteRawTrackToFile(this QobuzApiService s, string trackId, string trackPath, AudioQuality bitrate, CancellationToken token = default)
    {
        // Bound a single attempt so a stalled CDN connection fails (and is retried by the
        // caller) instead of hanging forever now that the client has no global timeout.
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        attemptCts.CancelAfter(TimeSpan.FromMinutes(10));
        var attemptToken = attemptCts.Token;

        using HttpResponseMessage response = await s.GetTrackResponse(trackId, bitrate, attemptToken);
        long? expectedLength = response.Content.Headers.ContentLength;

        // Stream to a temporary file and only move it into place once it is fully written and
        // verified, so an interrupted or errored download never leaves a 0-byte or truncated
        // file at the real destination for Lidarr to import.
        var tempPath = trackPath + ".part";
        try
        {
            await using (FileStream fileStream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (Stream httpStream = await response.Content.ReadAsStreamAsync(attemptToken))
            {
                await httpStream.CopyToAsync(fileStream, attemptToken);
                await fileStream.FlushAsync(attemptToken);
            }

            if (expectedLength.HasValue)
            {
                long actualLength = new FileInfo(tempPath).Length;
                if (actualLength != expectedLength.Value)
                    throw new IOException($"Incomplete download for track {trackId}: server reported {expectedLength.Value} bytes but only {actualLength} were written.");
            }

            File.Move(tempPath, trackPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort cleanup; nothing actionable if the temp file can't be removed
        }
    }

    public static async Task ApplyMetadataToFile(this QobuzApiService s, string trackId, string trackPath, string lyrics = "", CancellationToken token = default)
    {
        using TagLib.File file = TagLib.File.Create(trackPath);
        await s.ApplyMetadataToTagLibFile(file, trackId, lyrics, token);
    }

    public static async Task<(string? plainLyrics, string? syncLyrics)?> FetchLyricsFromLRCLIB(string instance, string trackName, string artistName, string albumName, long duration, CancellationToken token = default)
    {
        var requestUrl = $"https://{instance}/api/get?artist_name={Uri.EscapeDataString(artistName)}&track_name={Uri.EscapeDataString(trackName)}&album_name={Uri.EscapeDataString(albumName)}&duration={duration}";
        var response = await _client.GetAsync(requestUrl, token);

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(token);
            var json = JObject.Parse(content);
            return (json["plainLyrics"]?.ToString(), json["syncedLyrics"]?.ToString());
        }

        return null;
    }

    public static async Task<byte[]> GetAlbumArtBytes(this QobuzApiService s, Album albumData, CancellationToken token = default)
    {
        using HttpRequestMessage message = new(HttpMethod.Get, albumData.Image.Large);
        HttpResponseMessage response = await _client.SendAsync(message, token);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new Exception($"The art for {albumData.Id} is unavailable.");
        }

        return await response.Content.ReadAsByteArrayAsync(token);
    }

    private static async Task<HttpResponseMessage> GetTrackResponse(this QobuzApiService s, string trackId, AudioQuality bitrate, CancellationToken token = default)
    {
        var urls = (s.GetTrackFileUrl(trackId, ((int)bitrate).ToString())) ?? throw new Exception($"Track ID {trackId} has no available media sources for bitrate {bitrate}.");
        if (urls.Sample ?? false)
            throw new Exception("Qobuz provided a sample. The user probably does not have access to this quality of track.");

        HttpRequestMessage message = new(HttpMethod.Get, urls.Url);
        // ResponseHeadersRead: stream the body to disk instead of buffering the whole (often
        // 50-150 MB) Hi-Res file in memory, especially with several tracks downloading at once.
        HttpResponseMessage response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
        // Guard against the CDN returning an error (expired/403 URL, 404, 429, 5xx): without this
        // the error body would be written straight into the .flac file.
        response.EnsureSuccessStatusCode();

        return response;
    }

    private static async Task ApplyMetadataToTagLibFile(this QobuzApiService s, TagLib.File track, string trackId, string lyrics = "", CancellationToken token = default)
    {
        var page = s.GetTrack(trackId, true);
        var albumPage = s.GetAlbum(page.Album.Id, true);

        byte[]? albumArt = null;
        try { albumArt = await s.GetAlbumArtBytes(albumPage, token); } catch (Exception) { }

        track.Tag.Title = page.CompleteTitle;
        track.Tag.Album = albumPage.CompleteTitle;
        track.Tag.Performers = [page.Performer.Name];
        track.Tag.AlbumArtists = albumPage.Artists.Select(x => x.Name).ToArray();
        DateTime releaseDate = page.ReleaseDateOriginal.GetValueOrDefault().DateTime;
        track.Tag.Year = (uint)releaseDate.Year;
        track.Tag.Track = (uint)page.TrackNumber;
        track.Tag.TrackCount = (uint)albumPage.TracksCount;
        track.Tag.Disc = (uint)page.MediaNumber;
        track.Tag.DiscCount = (uint)albumPage.MediaCount;
        if (albumPage.Genre != null && !string.IsNullOrEmpty(albumPage.Genre.Name))
            track.Tag.Genres = [ albumPage.Genre.Name ];

        if (albumArt != null)
            track.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(albumArt))];

        track.Tag.Lyrics = lyrics;

        track.Save();
    }
}
