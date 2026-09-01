using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Services;

namespace ArgonFetch.Tests
{
    public class DownloadUrlBuilderTests
    {
        private static MediaRenditionDto Rendition(UrlType urlType, string? convertTo = null) =>
            new()
            {
                Key = "abc123",
                Label = "1080p",
                FileExtension = ".mp4",
                MimeType = "video/mp4",
                UrlType = urlType,
                ConvertTo = convertTo
            };

        [Fact]
        public void PathFor_SendsCombinedRenditionsToTheMuxingEndpoint()
        {
            Assert.Equal("/api/Stream/Combined/abc123", DownloadUrlBuilder.PathFor(Rendition(UrlType.Combined)));
        }

        [Fact]
        public void PathFor_SendsSingleStreamsToTheMediaEndpoint()
        {
            Assert.Equal("/api/Stream/Media/abc123", DownloadUrlBuilder.PathFor(Rendition(UrlType.Media)));
        }

        [Fact]
        public void PathFor_AsksForTheConversionARenditionDeclares()
        {
            // Without this the MP3 rendition streams the source codec under an .mp3 name.
            Assert.Equal("/api/Stream/Media/abc123?format=mp3",
                DownloadUrlBuilder.PathFor(Rendition(UrlType.Media, convertTo: "mp3")));
        }

        [Fact]
        public void UrlFor_JoinsTheOriginWithoutDoublingTheSlash()
        {
            Assert.Equal("https://app.argonfetch.dev/api/Stream/Media/abc123",
                DownloadUrlBuilder.UrlFor("https://app.argonfetch.dev/", Rendition(UrlType.Media)));
        }
    }
}
