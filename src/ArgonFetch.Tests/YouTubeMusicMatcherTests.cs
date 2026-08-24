using ArgonFetch.Application.Services;

namespace ArgonFetch.Tests
{
    /// <summary>
    /// Pins the matching rules. Every bypass in the matcher is a scar from a real track that
    /// resolved to the wrong recording or to nothing at all, and until now none of them was
    /// held in place by anything.
    /// </summary>
    public class YouTubeMusicMatcherTests
    {
        private const long ThreeMinutesMs = 180_000;
        private const long ThreeMinutesSec = 180;

        private static MatchCandidate Candidate(
            string title,
            string? artist = "Rick Astley",
            long durationSec = ThreeMinutesSec,
            string details = "") => new(title, artist, durationSec, details);

        private static MatchCandidate? Match(
            IReadOnlyList<MatchCandidate> candidates,
            string title = "Never Gonna Give You Up",
            string artist = "Rick Astley",
            long durationMs = ThreeMinutesMs,
            bool officialShelf = false) =>
            YouTubeMusicMatcher.BestMatch(candidates, title, artist, durationMs, officialShelf);

        [Fact]
        public void BestMatch_TakesTheRecordingThatWasAskedFor()
        {
            var wanted = Candidate("Never Gonna Give You Up");

            Assert.Same(wanted, Match([wanted]));
        }

        [Fact]
        public void BestMatch_ReturnsNull_RatherThanTheWrongRecording()
        {
            // Nothing here is the requested song. A wrong file is worse than a failed fetch,
            // because the caller cannot tell it went wrong.
            var result = Match([Candidate("Together Forever"), Candidate("Whenever You Need Somebody")]);

            Assert.Null(result);
        }

        [Theory]
        [InlineData("Never Gonna Give You Up (Instrumental)")]
        [InlineData("Never Gonna Give You Up (Karaoke Version)")]
        [InlineData("Never Gonna Give You Up - Piano Cover")]
        [InlineData("Never Gonna Give You Up (Nightcore)")]
        [InlineData("Never Gonna Give You Up [Slowed + Reverb]")]
        public void BestMatch_RejectsReworksNobodyAskedFor(string title)
        {
            // These carry the exact title, the exact artist and near enough the exact length,
            // so nothing but the marker itself tells them apart from the real recording.
            Assert.Null(Match([Candidate(title)]));
        }

        [Fact]
        public void BestMatch_KeepsTheReworkWhenTheRequestIsForOne()
        {
            var remix = Candidate("Sonne (Remix)", artist: "Rammstein");

            Assert.Same(remix, Match([remix], title: "Sonne (Remix)", artist: "Rammstein"));
        }

        [Fact]
        public void BestMatch_MatchesWhenTheRequestSpellsOutAFeatureCreditAndTheCandidateDoesNot()
        {
            // Sources write the guest into the track name where YouTube Music puts it in
            // brackets, which the candidate then loses to normalisation.
            var candidate = Candidate("Stay", artist: "The Kid LAROI");

            Assert.Same(candidate, Match(
                [candidate],
                title: "Stay (feat. Justin Bieber)",
                artist: "The Kid LAROI"));
        }

        [Fact]
        public void BestMatch_RejectsAnUploadCreditedToSomebodyElse()
        {
            // The strongest signal against a cover: someone else uploaded it.
            Assert.Null(Match([Candidate("Never Gonna Give You Up", artist: "Some Karaoke Channel")]));
        }

        [Fact]
        public void BestMatch_AcceptsAMismatchedCreditOnTheOfficialShelf_WhenNoCandidateCarriesTheName()
        {
            // The songs shelf is YouTube Music's own catalogue, so a row there is a release
            // rather than an upload, and a differing credit is usually the same recording filed
            // under another name. Rejecting on it would throw the release away for good.
            var relabelled = Candidate("Never Gonna Give You Up", artist: "RickAstleyVEVO Official");

            Assert.Same(relabelled, Match([relabelled], officialShelf: true));
        }

        [Fact]
        public void BestMatch_StillPrefersTheRightCredit_WhenOneCandidateCarriesIt()
        {
            var wrongCredit = Candidate("Never Gonna Give You Up", artist: "Some Cover Channel");
            var rightCredit = Candidate("Never Gonna Give You Up", artist: "Rick Astley");

            Assert.Same(rightCredit, Match([wrongCredit, rightCredit], officialShelf: true));
        }

        [Fact]
        public void BestMatch_DoesNotCompareCreditsAcrossScripts()
        {
            // A romanised name and the original script share no words, which used to throw away
            // every real candidate. An upload in another script is not what a karaoke channel
            // looks like, so the other filters carry the weight here.
            var japanese = new MatchCandidate("Say It", "ヨルシカ", ThreeMinutesSec);

            Assert.Same(japanese, Match([japanese], title: "Say It", artist: "Yorushika"));
        }

        [Fact]
        public void BestMatch_IgnoresTheCreditWhenTheRequestHasNoRealArtist()
        {
            // "Unknown" reaches the matcher whenever a request carried no artist. Treating it as
            // a credit rejects every real result.
            var candidate = Candidate("Some Song", artist: "Whoever Uploaded It");

            Assert.Same(candidate, Match([candidate], title: "Some Song", artist: "Unknown"));
        }

        [Fact]
        public void BestMatch_PrefersTheCandidateClosestToTheAskedForLength()
        {
            var radioEdit = Candidate("Never Gonna Give You Up", durationSec: 200);
            var albumVersion = Candidate("Never Gonna Give You Up", durationSec: 181);

            Assert.Same(albumVersion, Match([radioEdit, albumVersion]));
        }

        [Fact]
        public void BestMatch_KeepsSearchOrderWhenLengthsAreEquallyClose()
        {
            // YouTube Music ranks the canonical upload first, and a second of noise must not
            // outrank that. Only a clearly better fit may.
            var first = Candidate("Never Gonna Give You Up", durationSec: 182);
            var second = Candidate("Never Gonna Give You Up", durationSec: 179);

            Assert.Same(first, Match([first, second]));
        }

        [Fact]
        public void BestMatch_IgnoresLengthWhenTheSourceReportsNone()
        {
            // YTMusicAPI returns no duration at all for some rows. Filtering on it then discards
            // every candidate and the track resolves to nothing.
            var noDuration = Candidate("Never Gonna Give You Up", durationSec: 0);

            Assert.Same(noDuration, Match([noDuration]));
        }

        [Fact]
        public void BestMatch_IgnoresLengthWhenTheRequestHasNone()
        {
            // Spotify's duration is scraped and can be missing; matching still has to work.
            var candidate = Candidate("Never Gonna Give You Up", durationSec: 400);

            Assert.Same(candidate, Match([candidate], durationMs: 0));
        }

        [Fact]
        public void BestMatch_RejectsACandidateFarFromTheAskedForLength()
        {
            // An hour-long upload carrying the right title is a mix, not the track.
            Assert.Null(Match([Candidate("Never Gonna Give You Up", durationSec: 3600)]));
        }

        [Fact]
        public void BestMatch_ToleratesASpellingVariantInTheTitle()
        {
            var candidate = Candidate("Tobbss", artist: "Someone");

            Assert.Same(candidate, Match([candidate], title: "Tobbs", artist: "Someone"));
        }

        [Fact]
        public void BestMatch_ReturnsNullForNoCandidates()
        {
            Assert.Null(Match([]));
        }

        [Theory]
        // A leading hyphen is a search operator - it tells YouTube to drop every result
        // containing the word, so the shelf came back empty for tracks named this way.
        [InlineData("Artist", "-topic", "Artist topic")]
        [InlineData("Artist", "Spider-Man Theme", "Artist Spider-Man Theme")]
        [InlineData("", "Song", "Song")]
        [InlineData("Artist", "", "Artist")]
        public void SearchQuery_StripsTheOperatorMeaningFromALeadingHyphen(string artist, string title, string expected)
        {
            Assert.Equal(expected, YouTubeMusicMatcher.SearchQuery(artist, title));
        }
    }
}
