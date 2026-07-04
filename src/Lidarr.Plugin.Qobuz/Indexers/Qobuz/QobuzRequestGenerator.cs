using System;
using System.Collections.Generic;
using System.Linq;
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

            chain.AddTier(GetRequests($"{searchCriteria.ArtistQuery} {searchCriteria.AlbumQuery}"));

            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(ArtistSearchCriteria searchCriteria)
        {
            var chain = new IndexerPageableRequestChain();

            chain.AddTier(GetRequests(searchCriteria.ArtistQuery));

            return chain;
        }

        // Qobuz stores titles with a typographic apostrophe (U+2019) while
        // MusicBrainz uses a straight one (U+0027); its search won't match one
        // against the other, so any apostrophe title returns zero results.
        // Dropping the apostrophe entirely matches regardless of which glyph is
        // stored, and Qobuz's keyword search tolerates its absence.
        private static readonly char[] ApostropheVariants = { '\'', '’', '‘', '`', 'ʼ' };

        private static string NormalizeSearchQuery(string query)
        {
            return new string(query.Where(c => Array.IndexOf(ApostropheVariants, c) < 0).ToArray());
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

            searchParameters = NormalizeSearchQuery(searchParameters);

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
