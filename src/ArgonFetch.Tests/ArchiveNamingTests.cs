using ArgonFetch.Application.Queries;

namespace ArgonFetch.Tests
{
    public class ArchiveNamingTests
    {
        [Fact]
        public void Unique_KeepsTheNameWhenNothingElseHasIt()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Assert.Equal("Artist - Song.webm", StreamArchiveQueryHandler.Unique("Artist - Song.webm", used));
        }

        [Fact]
        public void Unique_SeparatesTracksThatShareAName()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Assert.Equal("Song.webm", StreamArchiveQueryHandler.Unique("Song.webm", used));
            Assert.Equal("Song (2).webm", StreamArchiveQueryHandler.Unique("Song.webm", used));
            Assert.Equal("Song (3).webm", StreamArchiveQueryHandler.Unique("Song.webm", used));
        }

        [Fact]
        public void Unique_TreatsNamesDifferingOnlyInCaseAsTheSame()
        {
            // Windows and macOS would collide these, unpacking fewer files than the zip holds.
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            StreamArchiveQueryHandler.Unique("Song.webm", used);

            Assert.Equal("SONG (2).webm", StreamArchiveQueryHandler.Unique("SONG.webm", used));
        }
    }
}
