using ArgonFetch.Application.Services;

namespace ArgonFetch.Tests
{
    public class MediaFileNameTests
    {
        [Fact]
        public void For_NamesTheFileAfterTheRecording()
        {
            var tags = new MediaTags("Never Gonna Give You Up", "Rick Astley");

            Assert.Equal("Rick Astley - Never Gonna Give You Up.webm", MediaFileName.For(tags, ".webm"));
        }

        [Theory]
        [InlineData("AC/DC", "Thunderstruck", "ACDC - Thunderstruck.mp3")]
        [InlineData("Someone", "What? Really: Yes", "Someone - What Really Yes.mp3")]
        [InlineData("Artist", "Title\\With\\Slashes", "Artist - TitleWithSlashes.mp3")]
        public void For_DropsCharactersAFilesystemRefuses(string artist, string title, string expected)
        {
            // Stripped rather than substituted: "AC_DC" is not what anyone wanted either.
            Assert.Equal(expected, MediaFileName.For(new MediaTags(title, artist), ".mp3"));
        }

        [Fact]
        public void For_DoesNotRepeatTheArtistTheTitleAlreadyNames()
        {
            // YouTube titles are usually written "Artist - Song" to begin with.
            var tags = new MediaTags("Rick Astley - Never Gonna Give You Up", "Rick Astley");

            Assert.Equal("Rick Astley - Never Gonna Give You Up.mp4", MediaFileName.For(tags, ".mp4"));
        }

        [Fact]
        public void For_StillPrefixesWhenTheTitleOnlyResemblesTheArtist()
        {
            var tags = new MediaTags("Astley Forever", "Rick Astley");

            Assert.Equal("Rick Astley - Astley Forever.mp4", MediaFileName.For(tags, ".mp4"));
        }

        [Fact]
        public void For_FallsBackWhenTheSourceNamedNothing()
        {
            Assert.Equal("download.mp3", MediaFileName.For(MediaTags.None, ".mp3"));
            Assert.Equal("download.mp3", MediaFileName.For(new MediaTags("   ", null), ".mp3"));
        }

        [Fact]
        public void For_UsesWhicheverHalfTheSourceHas()
        {
            Assert.Equal("Just A Title.mp3", MediaFileName.For(new MediaTags("Just A Title", null), ".mp3"));
            Assert.Equal("Just An Artist.mp3", MediaFileName.For(new MediaTags(null, "Just An Artist"), ".mp3"));
        }

        [Fact]
        public void For_KeepsTheNameShortEnoughToSave()
        {
            var name = MediaFileName.For(new MediaTags(new string('x', 400), "Artist"), ".mp3");

            // Most filesystems stop at 255 bytes, and the extension has to fit too.
            Assert.True(name.Length < 140, $"name was {name.Length} characters");
            Assert.EndsWith(".mp3", name);
        }

        [Fact]
        public void ContentDisposition_CarriesTheNameTwiceSoBothKindsOfClientCanReadIt()
        {
            var header = MediaFileName.ContentDisposition(new MediaTags("Sonne", "Rammstein"), ".webm");

            Assert.Equal("attachment; filename=\"Rammstein - Sonne.webm\"; filename*=UTF-8''Rammstein%20-%20Sonne.webm", header);
        }

        [Fact]
        public void ContentDisposition_KeepsAHeaderLegalWhenTheNameIsNotAscii()
        {
            var header = MediaFileName.ContentDisposition(new MediaTags("言って。", "ヨルシカ"), ".webm");

            // The quoted form must not carry raw non-ASCII, and must not break out of its quotes.
            var quoted = header.Split('"')[1];
            Assert.All(quoted, c => Assert.InRange(c, ' ', '~'));
            Assert.Contains("filename*=UTF-8''", header);
            Assert.Contains(Uri.EscapeDataString("言って。"), header);
        }

    }
}
