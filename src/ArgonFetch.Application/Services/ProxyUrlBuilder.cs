using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using Microsoft.AspNetCore.Http;

namespace ArgonFetch.Application.Services
{
    public interface IProxyUrlBuilder
    {
        StreamReferenceDto? BuildProxyReferences(
            StreamingUrlDto? originalUrls,
            IMediaUrlCacheService cacheService,
            bool forceAudio = false,
            string? proxy = null);
    }

    public class ProxyUrlBuilder : IProxyUrlBuilder
    {
        public StreamReferenceDto? BuildProxyReferences(
            StreamingUrlDto? originalUrls,
            IMediaUrlCacheService cacheService,
            bool forceAudio = false,
            string? proxy = null)
        {
            if (originalUrls == null)
                return null;

            var proxyReferences = new StreamReferenceDto
            {
                UrlType = UrlType.Media
            };

            // Determine if these are audio URLs
            // Check extensions OR if descriptions contain "audio" OR forced audio mode
            bool isAudio = forceAudio ||
                          IsAudioFormat(originalUrls.BestQualityFileExtension) ||
                          IsAudioFormat(originalUrls.MediumQualityFileExtension) ||
                          IsAudioFormat(originalUrls.WorstQualityFileExtension) ||
                          ContainsAudioIndicator(originalUrls.BestQualityDescription) ||
                          ContainsAudioIndicator(originalUrls.MediumQualityDescription) ||
                          ContainsAudioIndicator(originalUrls.WorstQualityDescription);

            // Build proxy reference for best quality
            if (!string.IsNullOrEmpty(originalUrls.BestQuality))
            {
                var (extension, mimeType) = Describe(originalUrls.BestQualityFileExtension, isAudio);
                proxyReferences.BestQualityKey = cacheService.CacheSingleUrl(originalUrls.BestQuality, isAudio, mimeType, proxy);
                proxyReferences.BestQualityDescription = originalUrls.BestQualityDescription;
                proxyReferences.BestQualityFileExtension = extension;
                proxyReferences.BestQualityMimeType = mimeType;
            }

            // Build proxy reference for medium quality
            if (!string.IsNullOrEmpty(originalUrls.MediumQuality))
            {
                var (extension, mimeType) = Describe(originalUrls.MediumQualityFileExtension, isAudio);
                proxyReferences.MediumQualityKey = cacheService.CacheSingleUrl(originalUrls.MediumQuality, isAudio, mimeType, proxy);
                proxyReferences.MediumQualityDescription = originalUrls.MediumQualityDescription;
                proxyReferences.MediumQualityFileExtension = extension;
                proxyReferences.MediumQualityMimeType = mimeType;
            }

            // Build proxy reference for worst quality
            if (!string.IsNullOrEmpty(originalUrls.WorstQuality))
            {
                var (extension, mimeType) = Describe(originalUrls.WorstQualityFileExtension, isAudio);
                proxyReferences.WorstQualityKey = cacheService.CacheSingleUrl(originalUrls.WorstQuality, isAudio, mimeType, proxy);
                proxyReferences.WorstQualityDescription = originalUrls.WorstQualityDescription;
                proxyReferences.WorstQualityFileExtension = extension;
                proxyReferences.WorstQualityMimeType = mimeType;
            }

            return proxyReferences;
        }

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

        private bool IsAudioFormat(string? fileExtension)
        {
            if (string.IsNullOrEmpty(fileExtension))
                return false;

            var audioExtensions = new[] { ".mp3", ".m4a", ".webm", ".ogg", ".opus", ".wav", ".aac", ".flac" };
            return audioExtensions.Any(ext => fileExtension.Equals(ext, StringComparison.OrdinalIgnoreCase));
        }

        private bool ContainsAudioIndicator(string? description)
        {
            if (string.IsNullOrEmpty(description))
                return false;

            var lowerDesc = description.ToLower();
            return lowerDesc.Contains("audio") && !lowerDesc.Contains("video");
        }
    }
}