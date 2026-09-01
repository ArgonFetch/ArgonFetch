using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;

namespace ArgonFetch.Application.Services
{
    /// <summary>
    /// Turns a rendition into the path that streams it. The REST response hands out a key and
    /// leaves this to the caller; a tool call has no human to read the docs, so it resolves here.
    /// </summary>
    public static class DownloadUrlBuilder
    {
        public static string PathFor(MediaRenditionDto rendition) =>
            rendition.UrlType == UrlType.Combined
                ? $"/api/Stream/Combined/{rendition.Key}"
                : $"/api/Stream/Media/{rendition.Key}"
                  + (rendition.ConvertTo is null ? string.Empty : $"?format={rendition.ConvertTo}");

        public static string UrlFor(string origin, MediaRenditionDto rendition) =>
            origin.TrimEnd('/') + PathFor(rendition);
    }
}
