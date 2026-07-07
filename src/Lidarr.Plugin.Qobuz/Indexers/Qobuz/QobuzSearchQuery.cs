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

        // Edition/format qualifier tokens that commonly appear in MusicBrainz
        // titles but not in Qobuz album titles. Stripped ONLY in the relaxed
        // fallback tier, never in the strict primary query.
        private static readonly Regex QualifierTokens = new Regex(
            @"\b(ep|deluxe|remaster(ed)?|remix|edit|mix|version|expanded|anniversary|edition|feat\.?|featuring)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BracketedSegments = new Regex(
            @"\s*[\(\[].*?[\)\]]", RegexOptions.Compiled);

        private static readonly Regex ExtraWhitespace = new Regex(
            @"\s{2,}", RegexOptions.Compiled);

        // Removes apostrophe glyphs so straight/curly variants both match.
        public static string NormalizeSearchQuery(string query)
        {
            return new string(query.Where(c => Array.IndexOf(ApostropheVariants, c) < 0).ToArray());
        }

        // Produces a relaxed album title for the fallback search tier: drops
        // bracketed segments and standalone edition/format qualifier tokens.
        // May return the input unchanged (no qualifiers) or empty (title was
        // entirely bracketed); callers skip the relaxed tier in those cases.
        // Numerics, hyphens and interior punctuation are preserved — they are
        // load-bearing in real titles and are not the failure mode.
        public static string RelaxAlbumTitle(string album)
        {
            if (string.IsNullOrWhiteSpace(album))
            {
                return string.Empty;
            }

            var s = BracketedSegments.Replace(album, " ");
            s = QualifierTokens.Replace(s, " ");
            s = ExtraWhitespace.Replace(s, " ");
            return s.Trim().Trim('-').Trim();
        }
    }
}
