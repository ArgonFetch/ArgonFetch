using ArgonFetch.Application.Services;

namespace ArgonFetch.Tests
{
    public class YtDlpErrorsTests
    {
        [Fact]
        public void IsDrmProtected_RecognisesTheRefusal()
        {
            Assert.True(YtDlpErrors.IsDrmProtected(["ERROR: [soundcloud] 253508261: This video is DRM protected"]));
        }

        [Fact]
        public void IsDrmProtected_IgnoresOtherFailures()
        {
            // A missing video really is missing; only DRM gets the different answer.
            Assert.False(YtDlpErrors.IsDrmProtected(["ERROR: [youtube] abc: Video unavailable"]));
        }

        [Theory]
        // Instagram's wording when it serves nothing to a signed-out request.
        [InlineData("ERROR: [Instagram] ABC: Instagram sent an empty media response. Check if this post is accessible in your browser without being logged-in.")]
        [InlineData("ERROR: [youtube] xyz: Sign in to confirm your age. Use --cookies-from-browser or --cookies.")]
        [InlineData("ERROR: [instagram] Requested content is not available, rate-limit reached or login required.")]
        public void NeedsSignedInSession_RecognisesARefusalACookiesFileWouldFix(string error)
        {
            Assert.True(YtDlpErrors.NeedsSignedInSession([error]));
        }

        [Theory]
        [InlineData("ERROR: [youtube] abc: Video unavailable")]
        [InlineData("ERROR: [soundcloud] 1: This video is DRM protected")]
        [InlineData(null)]
        public void NeedsSignedInSession_LeavesOtherFailuresAlone(string? error)
        {
            // Only refusals a cookies file can actually fix should say so; the rest are still
            // "not found", and telling someone to export cookies would waste their time.
            Assert.False(YtDlpErrors.NeedsSignedInSession(error is null ? null : [error]));
        }

        [Theory]
        [InlineData(null)]
        public void IsDrmProtected_HandlesNothingToRead(string[]? errorOutput)
        {
            Assert.False(YtDlpErrors.IsDrmProtected(errorOutput));
            Assert.False(YtDlpErrors.IsDrmProtected([]));
        }
    }
}
