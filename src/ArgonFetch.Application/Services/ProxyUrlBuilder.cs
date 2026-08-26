using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using Microsoft.AspNetCore.Http;

namespace ArgonFetch.Application.Services
{
    public interface IProxyUrlBuilder
    {
        /// <summary>
        /// Turns candidate formats into streamable renditions, best first. Each one is cached
        /// with the media type and proxy it has to be served with.
        /// </summary>
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

            // MP3 is offered explicitly rather than left implicit: it costs a conversion and a
            // generation of quality, so it belongs in the list beside the sources it is made
            // from - and stating its bitrate stops "MP3" from being the one option whose
            // quality nobody can see.
            //
            // Not offered when the source is already MP3, as SoundCloud's is: re-encoding it at
            // a higher bitrate produces a larger file that sounds worse, which is not a choice
            // worth putting in front of anyone.
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
                    // Not reported: the length of a re-encode is not known before it runs.
                    FileSizeBytes = null,
                    ConvertTo = "mp3"
                });
            }

            return renditions;
        }

        private static bool IsMp3(MediaRenditionDto rendition) =>
            rendition.MimeType.Equals("audio/mpeg", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The extension and media type the client will get. A recognised source container is
        /// reported as-is because those bytes are passed through untouched - claiming ".mp3"
        /// for Opus in WebM cost a needless FFmpeg pass and every tag the file carried.
        /// Unknown containers still convert, so they keep advertising the converted format.
        /// </summary>
        private static (string Extension, string MimeType) Describe(string? sourceExtension, bool isAudio)
        {
            var mimeType = MediaFormats.MimeTypeFor(sourceExtension, isAudio);

            if (mimeType == null)
                return isAudio ? (".mp3", "audio/mpeg") : (".mp4", "video/mp4");

            return (MediaFormats.NormalizeExtension(sourceExtension)!, mimeType);
        }

    }
}