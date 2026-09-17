using ArgonFetch.Application.Services;

namespace ArgonFetch.Tests
{
    public class PlaybackTrackPickerTests
    {
        private static PlaybackSource Video(
            string url,
            int height,
            string extension = "mp4",
            string codec = "avc1.640028",
            double bitrate = 1000,
            double? fps = null) =>
            new(url, $"{height}p", extension, codec, null, height * 16 / 9, height, fps, bitrate, null);

        private static PlaybackSource Audio(string url, string extension, double bitrate, string codec = "opus") =>
            new(url, $"{bitrate} kbps", extension, null, codec, null, null, null, bitrate, null);

        [Fact]
        public void PickVideo_KeepsEveryResolution()
        {
            var picked = PlaybackTrackPicker.PickVideo(
            [
                Video("a", 2160),
                Video("b", 1080),
                Video("c", 720),
                Video("d", 480),
                Video("e", 360),
                Video("f", 240),
            ]);

            // A download menu thins this out; a quality switcher must not.
            Assert.Equal(6, picked.Count);
            Assert.Equal(2160, picked[0].Height);
            Assert.Equal(240, picked[^1].Height);
        }

        [Fact]
        public void PickVideo_KeepsOneStreamPerResolutionAndContainer()
        {
            var picked = PlaybackTrackPicker.PickVideo(
            [
                Video("low", 1080, bitrate: 2000),
                Video("high", 1080, bitrate: 4000),
            ]);

            Assert.Single(picked);
            Assert.Equal("high", picked[0].Url);
        }

        [Fact]
        public void PickVideo_PrefersTheContainerEveryBrowserDecodes()
        {
            var picked = PlaybackTrackPicker.PickVideo(
            [
                Video("webm", 1080, extension: "webm", codec: "vp9", bitrate: 4000),
                Video("mp4", 1080, extension: "mp4", codec: "avc1.640028", bitrate: 2000),
            ]);

            Assert.Equal("mp4", picked[0].Url);
        }

        [Fact]
        public void PickVideo_DropsSourcesWithoutAUrl()
        {
            var picked = PlaybackTrackPicker.PickVideo([Video(string.Empty, 720), Video("ok", 480)]);

            Assert.Single(picked);
            Assert.Equal("ok", picked[0].Url);
        }

        [Fact]
        public void PickAudio_OrdersByBitrateAndCollapsesNearDuplicates()
        {
            var picked = PlaybackTrackPicker.PickAudio(
            [
                Audio("opus-low", "webm", 49),
                Audio("opus-high", "webm", 129),
                Audio("opus-high-again", "webm", 130),
                Audio("aac", "m4a", 128, "mp4a.40.2"),
            ]);

            Assert.Equal("opus-high", picked[0].Url);
            Assert.DoesNotContain(picked, source => source.Url == "opus-high-again");
            Assert.Contains(picked, source => source.Url == "aac");
        }

        [Fact]
        public void ContentType_CarriesTheCodecs()
        {
            Assert.Equal(
                "video/mp4; codecs=\"avc1.640028\"",
                PlaybackTrackPicker.ContentType("video/mp4", "avc1.640028", null));

            Assert.Equal(
                "video/mp4; codecs=\"avc1.640028, mp4a.40.2\"",
                PlaybackTrackPicker.ContentType("video/mp4", "avc1.640028", "mp4a.40.2"));
        }

        [Fact]
        public void ContentType_IgnoresTheAbsentCodecSourcesSpellAsNone()
        {
            Assert.Equal("audio/webm; codecs=\"opus\"",
                PlaybackTrackPicker.ContentType("audio/webm", "none", "opus"));

            Assert.Equal("video/mp4", PlaybackTrackPicker.ContentType("video/mp4", "none", "none"));
        }

        [Fact]
        public void Label_NamesTheResolutionAndSaysWhenItIsSmooth()
        {
            Assert.Equal("1080p", PlaybackTrackPicker.Label(Video("a", 1080, fps: 30), isAudio: false));
            Assert.Equal("1080p60", PlaybackTrackPicker.Label(Video("a", 1080, fps: 60), isAudio: false));
            Assert.Equal("2160p (4K)", PlaybackTrackPicker.Label(Video("a", 2160, fps: 24), isAudio: false));
        }

        [Fact]
        public void Label_NamesTheBitrateForAudio()
        {
            Assert.Equal("129 kbps", PlaybackTrackPicker.Label(Audio("a", "webm", 129), isAudio: true));
        }
    }
}
