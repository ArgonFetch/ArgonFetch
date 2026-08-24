using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Services;

namespace ArgonFetch.Tests
{
    public class MediaContentIdentifierTests
    {
        [Theory]
        [InlineData("https://www.youtube.com/playlist?list=PLFgquLnL59alCl", ContentType.Playlist)]
        [InlineData("https://www.youtube.com/playlist?list=RDabc123", ContentType.YouTubeRadio)]
        // A watch link that also names a list is one video seen in that list's context, which is
        // what YouTube plays. Reading it as the list handed back hundreds of entries to someone
        // who asked for one video.
        [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PLFgquLnL59alCl", ContentType.Media)]
        [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", ContentType.Media)]
        public async Task IdentifyContent_TellsAListFromAVideoInOne(string url, ContentType expected)
        {
            Assert.Equal(expected, await MediaContentIdentifierService.IdentifyContent(url, Platform.YouTube));
        }

        [Theory]
        [InlineData("https://soundcloud.com/artist/sets/an-album", ContentType.Playlist)]
        [InlineData("https://soundcloud.com/artist/a-track", ContentType.Media)]
        public async Task IdentifyContent_ReadsASoundCloudSet(string url, ContentType expected)
        {
            Assert.Equal(expected, await MediaContentIdentifierService.IdentifyContent(url, Platform.SoundCloud));
        }

        [Theory]
        // SoundCloud lists a set as bare links and nothing else, so without this every row of a
        // 329-track set read "Unknown" and there was no way to pick one.
        [InlineData("https://soundcloud.com/sweet-medicine/sweet-medicine-w-odyssee-breezin", "Sweet Medicine W Odyssee Breezin")]
        [InlineData("https://soundcloud.com/artist/some_track_name", "Some Track Name")]
        [InlineData("https://example.com/", null)]
        [InlineData("not a url", null)]
        [InlineData(null, null)]
        public void NameFromUrl_NamesARowAfterItsOwnLink(string? url, string? expected)
        {
            Assert.Equal(expected, GetMediaQueryHandler.NameFromUrl(url));
        }
    }
}
