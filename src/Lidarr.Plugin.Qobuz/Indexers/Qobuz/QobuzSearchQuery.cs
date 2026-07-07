using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Indexers.Qobuz
{
    // Pure, dependency-free helpers for shaping Qobuz search queries.
    // Separated from QobuzRequestGenerator so the string logic can be unit-tested
    // without pulling in the Lidarr indexer stack.
    public static class QobuzSearchQuery
    {
        // Qobuz stores titles with a typographic apostrophe (U+2019) while
        // MusicBrainz uses a straight one (U+0027); its search won't match one
        // against the other, so any apostrophe title returns zero results.
        // Dropping the apostrophe entirely matches regardless of which glyph is
        // stored, and Qobuz's keyword search tolerates its absence.
        private static readonly char[] ApostropheVariants = { '\'', '’', '‘', '`', 'ʼ' };

        // Removes apostrophe glyphs so straight/curly variants both match.
        public static string NormalizeSearchQuery(string query)
        {
            return new string(query.Where(c => Array.IndexOf(ApostropheVariants, c) < 0).ToArray());
        }

        // Implemented in Task 3 (its regex fields are introduced there too).
        public static string RelaxAlbumTitle(string album)
        {
            throw new NotImplementedException();
        }
    }
}
