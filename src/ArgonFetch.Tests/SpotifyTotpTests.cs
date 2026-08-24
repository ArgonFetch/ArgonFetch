using ArgonFetch.Application.Services;

namespace ArgonFetch.Tests
{
    /// <summary>
    /// The one-time password the Spotify web player signs its token request with. Worth pinning
    /// down: the whole playlist path fails at the first request if a byte of this is wrong, and
    /// the failure looks like a network problem rather than a wrong password.
    /// </summary>
    public class SpotifyTotpTests
    {
        // Shape and length of a real secret, without being one - the live secret rotates and is
        // fetched at runtime, so a copy here would only go stale.
        private static readonly int[] Secret =
            [12, 56, 76, 33, 88, 44, 88, 33, 78, 78, 11, 66, 22, 22, 55, 69, 54];

        [Theory]
        [InlineData(1700000000L, "863172")]
        [InlineData(1735689600L, "318982")]
        public void GenerateTotp_MatchesTheWebPlayersAlgorithm(long unixSeconds, string expected)
        {
            Assert.Equal(expected, SpotifyWebPlayerClient.GenerateTotp(Secret, unixSeconds));
        }

        [Fact]
        public void GenerateTotp_HoldsForThirtySecondsAtATime()
        {
            // The step is what makes Spotify's clock worth asking for: a server that is a minute
            // out mints a code from the wrong window and is refused.
            // 1700000010 is itself a window boundary, so the window it opens runs to ...039.
            var atStart = SpotifyWebPlayerClient.GenerateTotp(Secret, 1700000010);

            Assert.Equal(atStart, SpotifyWebPlayerClient.GenerateTotp(Secret, 1700000039));
            Assert.NotEqual(atStart, SpotifyWebPlayerClient.GenerateTotp(Secret, 1700000040));
        }

        [Fact]
        public void GenerateTotp_IsAlwaysSixDigits()
        {
            for (var offset = 0; offset < 40; offset++)
            {
                var code = SpotifyWebPlayerClient.GenerateTotp(Secret, 1700000000 + (offset * 30L));

                Assert.Equal(6, code.Length);
                Assert.All(code, character => Assert.True(char.IsAsciiDigit(character)));
            }
        }
    }
}
