using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using Microsoft.AspNetCore.Http;

namespace ArgonFetch.Application.Services
{
    public interface IProxyUrlBuilder
    {
        List<MediaRenditionDto> BuildRenditions(
            IEnumerable<RenditionSource> sources,
            IMediaUrlCacheService cacheService,
            bool isAudio,
            string? proxy = null,
            MediaTags? tags = null);
    }

    public class ProxyUrlBuilder : IProxyUrlBuilder
    {
        public List<MediaRenditionDto> BuildRenditions(
            IEnumerable<RenditionSource> sources,
            IMediaUrlCacheService cacheService,
            bool isAudio,
            string? proxy = null,
            MediaTags? tags = null)
        {
            var renditions = new List<MediaRenditionDto>();

            foreach (var source in sources)
            {
                var (extension, mimeType) = Describe(source.Extension, isAudio);

                renditions.Add(new MediaRenditionDto
                {
                    Key = cacheService.CacheSingleUrl(source.Url, isAudio, mimeType, proxy, tags),
                    Label = RenditionPicker.Label(source, isAudio),
                    Description = source.Description,
                    FileExtension = extension,
                    MimeType = mimeType,
                    UrlType = UrlType.Media,
                    FileSizeBytes = source.FileSizeBytes,
                    Height = isAudio ? null : source.Height,
                    Bitrate = source.Bitrate
                });
            }

            if (isAudio && renditions.Count > 0 && !IsMp3(renditions[0]))
            {
                renditions.Add(new MediaRenditionDto
                {
                    Key = renditions[0].Key,
                    Label = $"{MediaFormats.Mp3BitrateKbps} kbps",
                    Description = $"Converted from {renditions[0].Label}",
                    FileExtension = ".mp3",
                    MimeType = "audio/mpeg",
                    UrlType = UrlType.Media,
                    Bitrate = MediaFormats.Mp3BitrateKbps,
                    FileSizeBytes = null,
                    ConvertTo = "mp3"
                });
            }

            return renditions;
        }

        private static bool IsMp3(MediaRenditionDto rendition) =>
            rendition.MimeType.Equals("audio/mpeg", StringComparison.OrdinalIgnoreCase);

        private static (string Extension, string MimeType) Describe(string? sourceExtension, bool isAudio)
        {
            var mimeType = MediaFormats.MimeTypeFor(sourceExtension, isAudio);

            if (mimeType == null)
                return isAudio ? (".mp3", "audio/mpeg") : (".mp4", "video/mp4");

            return (MediaFormats.NormalizeExtension(sourceExtension)!, mimeType);
        }

    }
}