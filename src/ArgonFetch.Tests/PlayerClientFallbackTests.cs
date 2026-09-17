using ArgonFetch.Application.Services;

namespace ArgonFetch.Tests
{
    public class PlayerClientFallbackTests
    {
        [Fact]
        public void ReadPlayerClients_FallsBackToTheBuiltInList()
        {
            Assert.Equal(MediaMetadataFetcher.DefaultPlayerClients, MediaMetadataFetcher.ReadPlayerClients(null));
            Assert.Equal(MediaMetadataFetcher.DefaultPlayerClients, MediaMetadataFetcher.ReadPlayerClients(""));
            Assert.Equal(MediaMetadataFetcher.DefaultPlayerClients, MediaMetadataFetcher.ReadPlayerClients("   "));
        }

        [Fact]
        public void ReadPlayerClients_TakesAListAndTidiesIt()
        {
            Assert.Equal(["ios", "tv"], MediaMetadataFetcher.ReadPlayerClients("ios,tv"));
            Assert.Equal(["ios", "tv"], MediaMetadataFetcher.ReadPlayerClients(" ios , tv "));
            Assert.Equal(["ios"], MediaMetadataFetcher.ReadPlayerClients("ios,,"));
        }

        [Fact]
        public void DefaultPlayerClients_AreOnesYouTubeDoesNotAskATokenOf()
        {
            // The web clients are the ones that get challenged, so listing one here would make the
            // fallback a second helping of the failure it exists to get past.
            Assert.All(MediaMetadataFetcher.DefaultPlayerClients,
                client => Assert.DoesNotContain("web", client, StringComparison.OrdinalIgnoreCase));

            Assert.NotEmpty(MediaMetadataFetcher.DefaultPlayerClients);
        }

        [Theory]
        [InlineData("ERROR: [youtube] Sign in to confirm you're not a bot")]
        [InlineData("ERROR: Please log in to continue")]
        [InlineData("Use --cookies-from-browser or --cookies for the authentication")]
        public void TheChallengeIsRecognised(string line)
        {
            // Recognising it is what triggers the retry; if this stops matching, the fallback
            // never runs and the viewer is told to go and find a cookies file instead.
            Assert.True(YtDlpErrors.NeedsSignedInSession([line]));
        }

        [Fact]
        public void AnOrdinaryFailureIsNotMistakenForTheChallenge()
        {
            Assert.False(YtDlpErrors.NeedsSignedInSession(["ERROR: Video unavailable"]));
            Assert.False(YtDlpErrors.NeedsSignedInSession([]));
            Assert.False(YtDlpErrors.NeedsSignedInSession(null));
        }
    }
}
