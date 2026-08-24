using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Services;
using Moq;

namespace ArgonFetch.Tests
{
    public class ProxyUrlBuilderTests
    {
        private static StreamingUrlDto AudioSource(string extension) => new()
        {
            BestQualityDescription = "251 - audio only (medium)",
            BestQuality = "https://example.googlevideo.com/videoplayback?itag=251",
            BestQualityFileExtension = extension,
        };

        [Fact]
        public void BuildProxyReferences_KeepsTheSourceContainerForAudio()
        {
            var builder = new ProxyUrlBuilder();

            var result = builder.BuildProxyReferences(AudioSource(".webm"), CacheStub(), forceAudio: true);

            // Opus in WebM used to be advertised as ".mp3", which forced a needless re-encode.
            Assert.Equal(".webm", result!.BestQualityFileExtension);
            Assert.Equal("audio/webm", result.BestQualityMimeType);
        }

        [Fact]
        public void BuildProxyReferences_FallsBackToMp3ForUnknownAudioContainers()
        {
            var builder = new ProxyUrlBuilder();

            var result = builder.BuildProxyReferences(AudioSource(".sph"), CacheStub(), forceAudio: true);

            // Unknown containers are still converted, so the converted format is what is promised.
            Assert.Equal(".mp3", result!.BestQualityFileExtension);
            Assert.Equal("audio/mpeg", result.BestQualityMimeType);
        }

        [Fact]
        public void BuildProxyReferences_CachesTheMimeTypeItAdvertised()
        {
            var cache = new Mock<IMediaUrlCacheService>();
            cache.Setup(c => c.CacheSingleUrl(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), null))
                .Returns("key");

            new ProxyUrlBuilder().BuildProxyReferences(AudioSource(".webm"), cache.Object, forceAudio: true);

            cache.Verify(c => c.CacheSingleUrl(It.IsAny<string>(), true, "audio/webm", null), Times.Once);
        }

        [Fact]
        public void BuildRenditions_OffersAnMp3Conversion_ForSourcesThatAreNotAlreadyMp3()
        {
            var renditions = new ProxyUrlBuilder().BuildRenditions(
                [new RenditionSource("https://example.test/opus", "251 - audio only", ".webm", null, 129, 3_400_000)],
                CacheStub(),
                isAudio: true);

            var converted = Assert.Single(renditions, r => r.ConvertTo == "mp3");

            Assert.Equal(".mp3", converted.FileExtension);
            Assert.Equal("192 kbps", converted.Label);

            // The conversion streams from the same cached source it is made from.
            Assert.Equal(renditions[0].Key, converted.Key);
        }

        [Fact]
        public void BuildRenditions_SkipsTheMp3Conversion_WhenTheSourceIsAlreadyMp3()
        {
            // SoundCloud serves MP3 directly. Re-encoding it at a higher bitrate produces a
            // bigger file that sounds worse, so there is nothing to offer.
            var renditions = new ProxyUrlBuilder().BuildRenditions(
                [new RenditionSource("https://example.test/mp3", "128 kbps", ".mp3", null, 128, 2_200_000)],
                CacheStub(),
                isAudio: true);

            Assert.DoesNotContain(renditions, r => r.ConvertTo != null);
        }

        [Fact]
        public void BuildRenditions_OffersNoConversionForVideo()
        {
            var renditions = new ProxyUrlBuilder().BuildRenditions(
                [new RenditionSource("https://example.test/mp4", "1080p", ".mp4", 1080, 4000, 80_000_000)],
                CacheStub(),
                isAudio: false);

            Assert.DoesNotContain(renditions, r => r.ConvertTo != null);
        }

        [Theory]
        [InlineData(".webm", true, "audio/webm")]
        [InlineData(".webm", false, "video/webm")]
        [InlineData("m4a", true, "audio/mp4")]
        [InlineData(".MP4", false, "video/mp4")]
        [InlineData(".sph", true, null)]
        [InlineData(null, true, null)]
        public void MimeTypeFor_MapsContainersToMediaTypes(string? extension, bool isAudio, string? expected)
        {
            Assert.Equal(expected, MediaFormats.MimeTypeFor(extension, isAudio));
        }

        private static IMediaUrlCacheService CacheStub()
        {
            var cache = new Mock<IMediaUrlCacheService>();
            cache.Setup(c => c.CacheSingleUrl(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>(), null))
                .Returns("key");

            return cache.Object;
        }
    }
}
