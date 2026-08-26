using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Exceptions;

namespace ArgonFetch.Application.Services
{
    public class MediaContentIdentifierService
    {
        public static async Task<ContentType> IdentifyContent(string query, Platform platform)
        {
            switch (platform)
            {
                case Platform.SearchTerm:
                    return ContentType.SearchTerm;

                case Platform.YouTube:
                    var uri = new Uri(query);
                    var url_parms = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    string? listId = url_parms.Get("list"); // null when the url carries no list

                    if (!string.IsNullOrEmpty(listId) && string.IsNullOrEmpty(url_parms.Get("v")))
                        return listId.StartsWith("RD") ? ContentType.YouTubeRadio : ContentType.Playlist;

                    return ContentType.Media;

                case Platform.SoundCloud:
                    var soundCloudPathSegments = new Uri(query).AbsolutePath.Trim('/').Split('/');
                    return soundCloudPathSegments.Contains("sets") ? ContentType.Playlist : ContentType.Media;

                case Platform.Unknown:
                    return ContentType.Unknown;

                default:
                    throw new UnknownContentTypeException();
            }
        }

        public static async Task<ContentType> IdentifyContent(string query)
        {
            var platform = PlatformIdentifierService.IdentifyPlatform(query);
            return await IdentifyContent(query, platform);
        }
    }
}
