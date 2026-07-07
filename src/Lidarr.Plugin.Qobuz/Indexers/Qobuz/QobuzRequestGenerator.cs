using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Plugin.Qobuz.API;

namespace NzbDrone.Core.Indexers.Qobuz
{
    public class QobuzRequestGenerator : IIndexerRequestGenerator
    {
        private const int PageSize = 100;
        private const int MaxPages = 15;
        public QobuzIndexerSettings Settings { get; set; }
        public Logger Logger { get; set; }

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            // this is a lazy implementation, just here so that lidarr has something to test against when saving settings
            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(GetRequests("never gonna give you up"));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(AlbumSearchCriteria searchCriteria)
        {
            var chain = new IndexerPageableRequestChain();
            var artist = searchCriteria.ArtistQuery;
            var album = searchCriteria.AlbumQuery;

            // Tier 1 — strict: artist + full title. Unchanged from prior behavior.
            // Highest precision; a title that works today never reaches the tiers below.
            chain.AddTier(GetRequests($"{artist} {album}"));

            // Tier 2 — relaxed: drop parentheticals + edition/format qualifiers.
            // Only runs if Tier 1 returned nothing (Lidarr stops at the first
            // non-empty tier). Skipped when relaxation is a no-op or empties the title.
            var relaxed = QobuzSearchQuery.RelaxAlbumTitle(album);
            if (relaxed.Length > 0 && !relaxed.Equals(album, StringComparison.OrdinalIgnoreCase))
            {
                chain.AddTier(GetRequests($"{artist} {relaxed}"));
            }

            // Tier 3 — artist-only: let Lidarr's decision engine match the album
            // from the artist's catalog. Only runs if Tiers 1 & 2 both returned nothing.
            chain.AddTier(GetRequests(artist));

            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(ArtistSearchCriteria searchCriteria)
        {
            var chain = new IndexerPageableRequestChain();

            chain.AddTier(GetRequests(searchCriteria.ArtistQuery));

            return chain;
        }

        private IEnumerable<IndexerRequest> GetRequests(string searchParameters)
        {
            // make sure we are logged in and have valid credentials
            // if we don't it should throw an error
            if (!QobuzAPI.Instance.Client.IsAppSecretValid())
            {
                QobuzAPI.Instance.PickSignInFromSettings(Settings, Logger);
            }

            if (QobuzAPI.Instance.Login == null)
            {
                throw new Exception("Qobuz login failed, please check your credentials in the indexer settings.");
            }

            searchParameters = QobuzSearchQuery.NormalizeSearchQuery(searchParameters);

            for (var page = 0; page < MaxPages; page++)
            {
                var data = new Dictionary<string, string>()
                {
                    ["query"] = searchParameters,
                    ["limit"] = $"{PageSize}",
                    ["offset"] = $"{page * PageSize}",
                };

                var url = QobuzAPI.Instance!.GetAPIUrl("/album/search", data);
                var req = new IndexerRequest(url, HttpAccept.Json);
                req.HttpRequest.Method = System.Net.Http.HttpMethod.Get;
                req.HttpRequest.Headers.Add("X-App-ID", $"{QobuzAPI.Instance.Client.AppId}");
                req.HttpRequest.Headers.Add("X-User-Auth-Token", $"{QobuzAPI.Instance.Login.AuthToken}");
                yield return req;
            }
        }
    }
}
