using System.Text.RegularExpressions;

namespace ArgonFetch.Application.Services
{
    /// <summary>
    /// A YouTube Music search result, reduced to what matching needs.
    /// </summary>
    /// <param name="Title">Candidate track title.</param>
    /// <param name="Artist">Credited artist, if the source gave one.</param>
    /// <param name="DurationSec">Length in seconds, 0 when unknown.</param>
    /// <param name="Details">Release text (album, credits). Only its bracketed parts are read.</param>
    public record MatchCandidate(string Title, string? Artist, long DurationSec, string Details = "");

    /// <summary>
    /// Picks the YouTube Music result that is actually the requested recording.
    /// <para>
    /// Ported from Snepilatch's YouTubeMusicSource. Taking the first search hit matches covers,
    /// karaoke versions and instrumentals as readily as the real track, so the request is scored
    /// against each candidate on title, artist and duration instead.
    /// </para>
    /// </summary>
    public static class YouTubeMusicMatcher
    {
        internal const long DurationToleranceSec = 30L;

        // Tighter than the tolerance used when a title matched. Nothing but the length is
        // identifying the track on that path, and another track by the same artist is often
        // within a few seconds of the one that was asked for.
        internal const long CreditOnlyToleranceSec = 4L;
        internal const long DurationBucketSec = 5L;
        internal const double MinTitleScore = 0.8;
        internal const string UnknownPlaceholder = "Unknown";

        private static readonly HashSet<string> ReworkMarkers = new(StringComparer.Ordinal)
        {
            "cover", "covers", "karaoke", "instrumental", "remix", "nightcore", "sped", "slowed",
            "reverb", "piano", "acoustic", "tribute", "parody", "mashup", "rendition", "remake",
            "unplugged", "orchestral", "lofi", "8d", "guitar", "violin", "flute", "solo",
        };

        private static readonly Regex Bracketed = new(@"[(\[]([^)\]]*)[)\]]", RegexOptions.Compiled);
        private static readonly Regex NonAlphanumeric = new(@"[^\p{L}\p{N} ]", RegexOptions.Compiled);
        private static readonly Regex BracketedRun = new(@"\(.*?\)|\[.*?]", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex LeadingHyphen = new(@"(^|\s)-+", RegexOptions.Compiled);

        /// <summary>
        /// Null rather than a guess: the wrong recording is worse than none, and the caller skips.
        /// </summary>
        public static MatchCandidate? BestMatch(
            IReadOnlyList<MatchCandidate> candidates,
            string wantTitle,
            string wantArtist,
            long durationMs,
            bool officialShelf = false) =>
            RankMatches(candidates, wantTitle, wantArtist, durationMs, officialShelf).FirstOrDefault();

        /// <summary>
        /// Every candidate that could be the requested recording, best first.
        /// <para>
        /// A caller with a way to check its pick - fetching it and reading the real duration -
        /// can walk this list instead of taking the first and hoping.
        /// </para>
        /// </summary>
        public static IReadOnlyList<MatchCandidate> RankMatches(
            IReadOnlyList<MatchCandidate> candidates,
            string wantTitle,
            string wantArtist,
            long durationMs,
            bool officialShelf = false)
        {
            var want = TitleWords(wantTitle);
            var wantArtistWords = Words(RealArtist(wantArtist));

            // Read the request the same way as the candidate, brackets included: asking for "Sonne
            // (Remix)" must keep the remix, and that marker only survives in the bracket-keeping split.
            var asked = new HashSet<string>(MarkerWords(wantTitle), StringComparer.Ordinal);
            asked.UnionWith(MarkerWords(wantArtist));

            // Title words only. Including the artist's words would let "- MEMcho Solo Ver. -"
            // count as plainer than the group recording, because the soloist is one of the
            // credited artists and so appears in asked.
            var askedTitleWords = new HashSet<string>(MarkerWords(wantTitle), StringComparer.Ordinal);

            var titled = candidates.Where(candidate =>
                (want.Count == 0 || TitleScore(candidate.Title, want) >= MinTitleScore) &&
                // The release text too, not just the title: an instrumental cut usually carries the
                // original's exact title and artist, and only the release it sits on says what it is.
                // Bracketed only, though - an album is where a release declares itself, and scanning
                // the whole run made a plain album name disqualify every candidate. ReworkMarkers
                // holds bare instrument nouns, so "Guitar Songs" or "Piano Man" read as reworks and
                // the track became unresolvable on this source.
                !AddsRework($"{candidate.Title} {BracketedIn(candidate.Details)}", asked))
                .ToList();

            // The songs shelf is YouTube Music's own catalogue, so a row on it is a release rather
            // than somebody's upload, and a credit that does not match ours is usually the same
            // recording filed under a different name. When no candidate carries the wanted name at
            // all the credit is telling us nothing, and rejecting on it throws the release away for
            // good.
            //
            // The videos shelf is where anyone can upload, which is what the artist check is for, so
            // there a miss stays a miss. Same on any shelf as soon as one candidate does carry the
            // name: the credit discriminates again, and the ones that lack it lose.
            var byArtist = titled.Where(c => ArtistMatches(c.Artist, wantArtistWords)).ToList();
            var viable = officialShelf && byArtist.Count == 0 ? titled : byArtist;

            var ordered = viable.Select((candidate, index) => (candidate, index));

            // Duration is a tiebreaker, not a requirement. Not every search backend reports it,
            // and filtering on it when it is missing discards every candidate and resolves
            // nothing.
            var timed = viable.Where(c => c.DurationSec > 0).ToList();

            if (durationMs <= 0L || timed.Count == 0)
            {
                return ordered
                    .OrderBy(x => ExtraWords(x.candidate.Title, askedTitleWords))
                    .ThenBy(x => x.index)
                    .Select(x => x.candidate)
                    .ToList();
            }

            var wantSec = durationMs / 1000;

            // YouTube Music ranks the canonical upload first. An instrumental runs to the same length
            // as the vocal take, so picking purely by the smallest duration difference let a second or
            // two of noise outrank that order; only a clearly better fit (a whole bucket) may.
            return ordered
                .Where(x => x.candidate.DurationSec > 0 &&
                            Math.Abs(x.candidate.DurationSec - wantSec) <= DurationToleranceSec)
                .OrderBy(x => Math.Abs(x.candidate.DurationSec - wantSec) / DurationBucketSec)
                .ThenBy(x => ExtraWords(x.candidate.Title, askedTitleWords))
                .ThenBy(x => x.index)
                .Select(x => x.candidate)
                .ToList();
        }

        /// <summary>
        /// Candidates that match on credit alone, best first, for a caller that can verify its
        /// pick another way.
        /// <para>
        /// A release is often filed under a translated name - Spotify says "REVENGE OF B" where
        /// YouTube Music says the Japanese original - and the two titles share no words at all,
        /// so title matching rejects every candidate and the track cannot be fetched. The credit
        /// still matches, and a duration read from the fetched result settles which one it is,
        /// so this is only safe for a caller that performs that check.
        /// </para>
        /// </summary>
        public static IReadOnlyList<MatchCandidate> RankByCreditOnly(
            IReadOnlyList<MatchCandidate> candidates,
            string wantArtist,
            long durationMs = 0L)
        {
            var wantArtistWords = Words(RealArtist(wantArtist));

            if (wantArtistWords.Count == 0)
                return [];

            var asked = new HashSet<string>(MarkerWords(wantArtist), StringComparer.Ordinal);

            var viable = candidates
                .Where(c => ArtistMatches(c.Artist, wantArtistWords, allowScriptMismatch: false) &&
                            !AddsRework($"{c.Title} {BracketedIn(c.Details)}", asked))
                .ToList();

            var timed = viable.Where(c => c.DurationSec > 0).ToList();

            if (durationMs <= 0L || timed.Count == 0)
                return viable;

            var wantSec = durationMs / 1000;

            // Closest first rather than search order: the artist's other tracks are in this list
            // too, and one of them being a few seconds from the right length is not a reason to
            // prefer it to an exact match.
            return timed
                .Where(c => Math.Abs(c.DurationSec - wantSec) <= CreditOnlyToleranceSec)
                .OrderBy(c => Math.Abs(c.DurationSec - wantSec))
                .ToList();
        }

        /// <summary>
        /// Words the candidate's title carries that nobody asked for.
        /// <para>
        /// Search returns the radio edit, the remaster and the twelve-inch mix alongside the
        /// album version, all with the right title and the right credit. Where no duration is
        /// available to separate them, the plainest title is the one that was asked for.
        /// Counted with brackets kept, because that is where the qualifier lives.
        /// </para>
        /// </summary>
        internal static int ExtraWords(string candidateTitle, ISet<string> asked) =>
            MarkerWords(candidateTitle).Count(word => !asked.Contains(word));

        /// <summary>
        /// True when the candidate advertises a rework the request never asked for. Asking for a
        /// track that genuinely is a remix keeps working, because the marker is then in asked too.
        /// </summary>
        internal static bool AddsRework(string candidateTitle, ISet<string> asked) =>
            MarkerWords(candidateTitle).Any(w => ReworkMarkers.Contains(w) && !asked.Contains(w));

        /// <summary>
        /// The bracketed parts of a run of release text - "Artist - Album (Instrumental) - 3:45"
        /// yields "Instrumental".
        /// </summary>
        internal static string BracketedIn(string text) =>
            string.Join(" ", Bracketed.Matches(text ?? string.Empty).Select(m => m.Groups[1].Value));

        /// <summary>
        /// The artist to treat as asked for. Blanks the placeholder that reaches here whenever a
        /// request carried no artist name: it is not a credit, so requiring candidates to share a
        /// word with it rejects every real result and the track stops resolving at all.
        /// </summary>
        internal static string RealArtist(string artist) =>
            string.Equals(artist, UnknownPlaceholder, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : artist ?? string.Empty;

        /// <summary>
        /// Like Words but keeps bracketed text - which is where a release says what it is.
        /// Dropping it made "(Instrumental)" indistinguishable from the real recording: same
        /// title, same artist, same length.
        /// </summary>
        private static HashSet<string> MarkerWords(string s) =>
            new(NonAlphanumeric
                    .Replace((s ?? string.Empty).ToLowerInvariant(), " ")
                    .Split(' ')
                    .Where(w => !string.IsNullOrWhiteSpace(w)),
                StringComparer.Ordinal);

        /// <summary>
        /// The strongest signal against a cover: a piano rendition is uploaded by whoever played it,
        /// not by the artist. A source may credit several artists where YouTube credits one, so
        /// sharing a single name is enough. An unknown artist on either side cannot rule anything out.
        /// </summary>
        /// <param name="allowScriptMismatch">
        /// Whether credits written in different scripts may be assumed to match. They may when a
        /// title match already identified the recording; they may not when the credit is the only
        /// evidence, or a karaoke label credited in one script passes for any artist in another.
        /// </param>
        internal static bool ArtistMatches(string? candidateArtist, ISet<string> wantArtist, bool allowScriptMismatch = true)
        {
            if (wantArtist.Count == 0) return true;

            var have = Words(candidateArtist ?? string.Empty);
            if (have.Count == 0) return true;

            // Two scripts cannot be compared by word overlap at all. Romanised names appear in the
            // original script on the other side, they share nothing, and every real candidate was
            // thrown away. Skipping the check costs nothing the other filters do not already cover:
            // an upload in a different script from the artist we asked for is not what a cover or a
            // karaoke channel looks like.
            if (allowScriptMismatch && HasLatin(have) != HasLatin(wantArtist)) return true;

            return have.Any(h => wantArtist.Any(w => NearlyEqual(h, w)));
        }

        /// <summary>Normalize has already lowercased, so a Latin letter is enough to tell the scripts apart.</summary>
        private static bool HasLatin(IEnumerable<string> words) =>
            words.Any(word => word.Any(c => c >= 'a' && c <= 'z'));

        /// <summary>
        /// Share of the wanted title's words the candidate carries.
        /// <para>
        /// Counted with the candidate's brackets kept. The two sides write a version qualifier
        /// differently - Spotify hyphenates it into the name, "World is Mine - Kaguya&amp;Yachiyo
        /// Runami ver. - CPK! Remix", where YouTube Music brackets it - so dropping the brackets
        /// erased the half of the candidate that the request was mostly made of, and an exact
        /// match scored a third. Keeping them cannot cost a match either way: the score measures
        /// how much of the wanted title the candidate carries, so words the candidate has and
        /// nobody asked for were never counted against it.
        /// </para>
        /// </summary>
        internal static double TitleScore(string candidateTitle, ISet<string> want)
        {
            if (want.Count == 0) return 0.0;

            var have = MarkerWords(candidateTitle);
            return want.Count(w => have.Any(h => NearlyEqual(h, w))) / (double)want.Count;
        }

        /// <summary>Equal, or one edit apart, so a spelling variant like "Tobbs" / "Tobbss" still matches.</summary>
        private static bool NearlyEqual(string a, string b)
        {
            if (a == b) return true;
            if (Math.Min(a.Length, b.Length) < 4 || Math.Abs(a.Length - b.Length) > 1) return false;

            var (longer, shorter) = a.Length >= b.Length ? (a, b) : (b, a);
            var i = 0;
            var j = 0;
            var edits = 0;

            while (i < longer.Length && j < shorter.Length)
            {
                if (longer[i] == shorter[j])
                {
                    i++;
                    j++;
                }
                else
                {
                    if (++edits > 1) return false;
                    i++;
                    if (longer.Length == shorter.Length) j++;
                }
            }

            return edits + (longer.Length - i) + (shorter.Length - j) <= 1;
        }

        /// <summary>
        /// What to search for, with the operator meaning taken out of a leading hyphen.
        /// <para>
        /// YouTube reads "-word" as "exclude everything containing word". A track whose name opens
        /// with a hyphen therefore asked search to drop every result carrying that word, and the
        /// shelf came back empty. Only a hyphen that opens a word is an operator, so a name like
        /// Spider-Man keeps its own.
        /// </para>
        /// </summary>
        public static string SearchQuery(string artist, string title)
        {
            var joined = string.Join(" ", new[] { artist, title }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return LeadingHyphen.Replace(joined, "$1").Trim();
        }

        /// <summary>
        /// The words of the wanted title a candidate has to carry, which stops at the feature credit.
        /// <para>
        /// Some sources write the guest into the track name where YouTube Music puts it in brackets
        /// that Normalize then strips off the candidate, so a three-word wanted title was scored
        /// against a one-word candidate and never came near MinTitleScore. Only the part before the
        /// credit is required; a candidate that does spell the guest out still matches, because the
        /// score measures how much of the wanted title the candidate carries and never penalises
        /// extra words.
        /// </para>
        /// </summary>
        internal static HashSet<string> TitleWords(string title)
        {
            var beforeCredit = SplitOnFeature(Normalize(title));
            var words = Words(beforeCredit);
            return words.Count > 0 ? words : Words(title);
        }

        private static string SplitOnFeature(string normalized)
        {
            var featIndex = normalized.IndexOf(" feat ", StringComparison.Ordinal);
            var ftIndex = normalized.IndexOf(" ft ", StringComparison.Ordinal);

            var index = featIndex >= 0 && ftIndex >= 0
                ? Math.Min(featIndex, ftIndex)
                : Math.Max(featIndex, ftIndex);

            return index >= 0 ? normalized[..index] : normalized;
        }

        private static HashSet<string> Words(string s) =>
            new(Normalize(s).Split(' ').Where(w => !string.IsNullOrWhiteSpace(w)), StringComparer.Ordinal);

        internal static string Normalize(string s)
        {
            var lowered = (s ?? string.Empty).ToLowerInvariant();
            var withoutBrackets = BracketedRun.Replace(lowered, " ");
            var alphanumeric = NonAlphanumeric.Replace(withoutBrackets, " ");
            return Whitespace.Replace(alphanumeric, " ").Trim();
        }
    }
}
