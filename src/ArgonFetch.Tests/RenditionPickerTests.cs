using ArgonFetch.Application.Services;

namespace ArgonFetch.Tests
{
    public class RenditionPickerTests
    {
        private static RenditionSource Video(int height, double bitrate = 1000) =>
            new($"https://example.test/{height}", $"{height}p", ".mp4", height, bitrate, null);

        private static RenditionSource Audio(double bitrate, string extension = ".webm") =>
            new($"https://example.test/{bitrate}{extension}", $"{bitrate} audio", extension, null, bitrate, null);

        [Fact]
        public void PickVideo_KeepsOneEntryPerResolution_BestFirst()
        {
            var picked = RenditionPicker.PickVideo(
                [Video(1080, 4000), Video(1080, 2500), Video(2160), Video(720)]);

            Assert.Equal(["2160p (4K)", "1080p", "720p"], picked.Select(p => RenditionPicker.Label(p, isAudio: false)));

            // The better encode of the duplicated resolution is the one kept.
            Assert.Equal(4000, picked[1].Bitrate);
        }

        [Fact]
        public void PickVideo_SpreadsAcrossTheRange_KeepingBestAndWorst()
        {
            var picked = RenditionPicker.PickVideo(
                [Video(2160), Video(1440), Video(1080), Video(720), Video(480), Video(360), Video(144)]);

            Assert.Equal(4, picked.Count);

            // Taking the top four would offer no small download at all, which is the choice
            // someone on a slow connection is actually after.
            Assert.Equal("2160p (4K)", RenditionPicker.Label(picked[0], isAudio: false));
            Assert.Equal("144p", RenditionPicker.Label(picked[^1], isAudio: false));
        }

        [Fact]
        public void PickAudio_CollapsesBitratesWithinOneContainer_ButKeepsBothContainers()
        {
            // The order a caller hands in reflects Opus being worth more than AAC at the same
            // bitrate, so the Opus entry leads - but the M4A is a different file type, and
            // whether a player can read it is exactly the choice worth offering.
            var picked = RenditionPicker.PickAudio([Audio(128.9), Audio(129.5, ".m4a"), Audio(126, ".webm"), Audio(48)]);

            Assert.Equal([".webm", ".m4a", ".webm"], picked.Select(p => p.Extension));
            Assert.Equal(3, picked.Count);
            Assert.Equal(48, picked[^1].Bitrate);
        }

        [Fact]
        public void PickAudio_ReturnsEverythingWhenThereAreFewerStepsThanAsked()
        {
            var picked = RenditionPicker.PickAudio([Audio(160), Audio(70)]);

            Assert.Equal(2, picked.Count);
        }

        [Fact]
        public void Pick_ReturnsEmpty_WhenNothingIsStreamable()
        {
            Assert.Empty(RenditionPicker.PickVideo([]));
            Assert.Empty(RenditionPicker.PickAudio([new RenditionSource("", null, null, null, null, null)]));
        }

        [Fact]
        public void Label_FallsBackToBitrateThenDescription()
        {
            Assert.Equal("160 kbps", RenditionPicker.Label(Audio(160), isAudio: true));
            Assert.Equal("720p", RenditionPicker.Label(Video(720), isAudio: false));
            Assert.Equal(
                "some format",
                RenditionPicker.Label(new RenditionSource("u", "some format", null, null, null, null), isAudio: true));
        }
    }
}
