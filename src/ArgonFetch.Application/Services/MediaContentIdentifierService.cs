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
                    string listId = url_parms.Get("list"); // search for list property in url

                    if (!string.IsNullOrEmpty(listId))
                        return listId.StartsWith("RD") ? ContentType.YouTubeRadio : ContentType.Playlist;

                    return ContentType.Media;

                case Platform.Spotify:
                    var spotifyPathSegments = new Uri(query).AbsolutePath.Trim('/').Split('/');
                    if (spotifyPathSegments.Contains("playlist"))
                        return ContentType.Playlist;
                    else if (spotifyPathSegments.Contains("album"))
                        return ContentType.SpotifyAlbum;
                    else if (spotifyPathSegments.Contains("track"))
                        return ContentType.Media;
                    else
                        throw new UnknownContentTypeException();

                case Platform.SoundCloud:
                    var soundCloudPathSegments = new Uri(query).AbsolutePath.Trim('/').Split('/');
                    return soundCloudPathSegments.Contains("sets") ? ContentType.Playlist : ContentType.Media;

                case Platform.TikTok:
                    return ContentType.Media;

                case Platform.Unknown:
                    return ContentType.Unknown;
                //using (var httpClient = new HttpClient())
                //{
                //    var response = await httpClient.GetAsync(query);
                //    response.EnsureSuccessStatusCode();

                //    var contentType = response.Content.Headers.ContentType?.MediaType;

                //    return (contentType != null && (contentType.Contains("audio") || contentType.Contains("video")))
                //        ? ContentType.Media
                //        : ContentType.Url;
                //}

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
