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
        [InlineData(null)]
        public void IsDrmProtected_HandlesNothingToRead(string[]? errorOutput)
        {
            Assert.False(YtDlpErrors.IsDrmProtected(errorOutput));
            Assert.False(YtDlpErrors.IsDrmProtected([]));
        }
    }
}
