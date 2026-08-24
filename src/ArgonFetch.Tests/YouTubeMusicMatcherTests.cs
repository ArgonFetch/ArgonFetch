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
            // Not every search backend reports a duration. Filtering on it then discards
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

        [Fact]
        public void BestMatch_PrefersThePlainestTitleWhenNothingElseSeparatesCandidates()
        {
            // Search returns the radio edit above the album version. Where neither carries a
            // duration, without this the edit wins on search order alone.
            var radioEdit = Candidate("One More Time (Radio Edit)", artist: "Daft Punk", durationSec: 0);
            var albumVersion = Candidate("One More Time", artist: "Daft Punk", durationSec: 0);

            var best = Match([radioEdit, albumVersion], title: "One More Time", artist: "Daft Punk", durationMs: 0);

            Assert.Same(albumVersion, best);
        }

        [Fact]
        public void BestMatch_RejectsASoloRecordingOfAGroupTrack()
        {
            // A solo re-recording is a different performance, and search ranks these above the
            // group version whenever the soloist is one of the credited artists.
            const string group = "B小町 ルビー（CV：伊駒ゆりえ）、有馬かな（CV：潘めぐみ）、MEMちょ（CV：大久保瑠美）";
            var solo = new MatchCandidate("サインはB -MEMちょ Solo Ver.-", "B小町 MEMちょ（CV：大久保瑠美）", 0);

            Assert.Null(Match([solo], title: "サインはB", artist: group, durationMs: 0, officialShelf: true));
        }

        [Fact]
        public void RankByCreditOnly_KeepsATranslatedTitleThatSharesNoWords()
        {
            // Spotify says "REVENGE OF B" where YouTube Music says the Japanese original, and the
            // two share nothing to match on. The credit is all that is left, so the caller has to
            // confirm the pick by its length afterwards.
            const string wanted = "B小町, ルビー(CV:伊駒ゆりえ), 有馬かな(CV:潘めぐみ), MEMちょ(CV:大久保瑠美)";
            const string credited = "B小町 ルビー（CV：伊駒ゆりえ）、有馬かな（CV：潘めぐみ）、MEMちょ（CV：大久保瑠美）";

            var translated = new MatchCandidate("Bのリベンジ", credited, 0, credited);

            Assert.Null(Match([translated], title: "REVENGE OF B", artist: wanted, durationMs: 0, officialShelf: true));
            Assert.Contains(translated, YouTubeMusicMatcher.RankByCreditOnly([translated], wanted));
        }

        [Fact]
        public void RankByCreditOnly_StillRefusesReworksAndForeignCredits()
        {
            const string wanted = "B小町, ルビー(CV:伊駒ゆりえ)";
            const string credited = "B小町 ルビー（CV：伊駒ゆりえ）";

            var instrumental = new MatchCandidate("Bのリベンジ（instrumental）", credited, 0, credited);
            var someoneElse = new MatchCandidate("Bのリベンジ", "歌っちゃ王", 0, "歌っちゃ王");

            Assert.Empty(YouTubeMusicMatcher.RankByCreditOnly([instrumental, someoneElse], wanted));
        }

        [Fact]
        public void RankByCreditOnly_TakesTheClosestLengthRatherThanTheFirstThatFits()
        {
            // The artist's other tracks are in the same search, and one of them landing a few
            // seconds from the right length is not a reason to prefer it to an exact match.
            const string artist = "B小町 ルビー（CV：伊駒ゆりえ）";

            var nearby = new MatchCandidate("深海52Hz", artist, 175, artist);
            var exact = new MatchCandidate("Bのリベンジ", artist, 182, artist);

            var ranked = YouTubeMusicMatcher.RankByCreditOnly([nearby, exact], artist, durationMs: 182_000);

            Assert.Equal(exact, ranked.First());
            Assert.DoesNotContain(nearby, ranked);
        }

        [Fact]
        public void RankByCreditOnly_RefusesToGuessWhenNoArtistWasGiven()
        {
            // Without a credit there is nothing left to match on at all, and returning everything
            // would hand the caller a list of arbitrary tracks to pick from by length alone.
            var candidate = new MatchCandidate("Anything At All", "Whoever", 0);

            Assert.Empty(YouTubeMusicMatcher.RankByCreditOnly([candidate], "Unknown"));
            Assert.Empty(YouTubeMusicMatcher.RankByCreditOnly([candidate], ""));
        }

        [Fact]
        public void RankMatches_FindsATrackWhoseVersionSpotifyHyphenatesAndYouTubeBrackets()
        {
            // Real rows from a YouTube Music search, and the Spotify track that failed to
            // resolve against them. Spotify writes the version into the name after a hyphen;
            // YouTube Music brackets it and puts the romanisation after the Japanese original.
            const string wantTitle = "World is Mine - Kaguya&Yachiyo Runami ver. - CPK! Remix";
            const string wantArtist = "ryo (supercell), Kaguya(cv.Yuko Natsuyoshi), Yachiyo Runami(cv.Saori Hayami)";

            var asked = new MatchCandidate(
                "\u30EF\u30FC\u30EB\u30C9\u30A4\u30BA\u30DE\u30A4\u30F3 (\u304B\u3050\u3084&\u6708\u898B\u30E4\u30C1\u30E8 ver.) [CPK! Remix] - World Is Mine (Kaguya&Yachiyo Runami Ver.) [CPK! Remix]",
                "ryo (supercell), Kaguya(cv.Yuko Natsuyoshi), Yachiyo Runami(cv.Saori Hayami)",
                100);

            // The same remix sung by someone else, which is not what was asked for.
            var otherVersion = new MatchCandidate(
                "\u30EF\u30FC\u30EB\u30C9\u30A4\u30BA\u30DE\u30A4\u30F3 (Anime ver.) [CPK! Remix] - World Is Mine (Anime Ver.) [CPK! Remix] (feat. ChoKaguyaHime)",
                "supercell und Yachiyo Runami(cv.Saori Hayami)",
                100);

            var ranked = YouTubeMusicMatcher.RankMatches([otherVersion, asked], wantTitle, wantArtist, durationMs: 0);

            Assert.Equal(asked, ranked.FirstOrDefault());
        }

        [Fact]
        public void TitleScore_ReadsAQualifierTheCandidateKeepsInBrackets()
        {
            var want = YouTubeMusicMatcher.TitleWords("Sonne - Live Version");

            // Bracketed on the candidate, hyphenated on the request: the same words either way.
            Assert.Equal(1.0, YouTubeMusicMatcher.TitleScore("Sonne (Live Version)", want));
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
