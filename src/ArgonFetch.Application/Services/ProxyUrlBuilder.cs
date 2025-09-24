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
            bool forceAudio = false);
    }

    public class ProxyUrlBuilder : IProxyUrlBuilder
    {
        public StreamReferenceDto? BuildProxyReferences(
            StreamingUrlDto? originalUrls,
            IMediaUrlCacheService cacheService,
            bool forceAudio = false)
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
                var cacheKey = cacheService.CacheSingleUrl(originalUrls.BestQuality, isAudio);
                proxyReferences.BestQualityKey = cacheKey;
                proxyReferences.BestQualityDescription = originalUrls.BestQualityDescription;
                // Standardize file extension to mp3 for audio, mp4 for video
                proxyReferences.BestQualityFileExtension = isAudio ? ".mp3" : ".mp4";
            }

            // Build proxy reference for medium quality
            if (!string.IsNullOrEmpty(originalUrls.MediumQuality))
            {
                var cacheKey = cacheService.CacheSingleUrl(originalUrls.MediumQuality, isAudio);
                proxyReferences.MediumQualityKey = cacheKey;
                proxyReferences.MediumQualityDescription = originalUrls.MediumQualityDescription;
                // Standardize file extension to mp3 for audio, mp4 for video
                proxyReferences.MediumQualityFileExtension = isAudio ? ".mp3" : ".mp4";
            }

            // Build proxy reference for worst quality
            if (!string.IsNullOrEmpty(originalUrls.WorstQuality))
            {
                var cacheKey = cacheService.CacheSingleUrl(originalUrls.WorstQuality, isAudio);
                proxyReferences.WorstQualityKey = cacheKey;
                proxyReferences.WorstQualityDescription = originalUrls.WorstQualityDescription;
                // Standardize file extension to mp3 for audio, mp4 for video
                proxyReferences.WorstQualityFileExtension = isAudio ? ".mp3" : ".mp4";
            }

            return proxyReferences;
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