using ArgonFetch.Application.Dtos;
using Microsoft.AspNetCore.Http;

namespace ArgonFetch.Application.Services
{
    public interface IProxyUrlBuilder
    {
        StreamingUrlDto? BuildProxyUrls(
            StreamingUrlDto? originalUrls,
            HttpRequest httpRequest,
            IMediaUrlCacheService cacheService,
            bool forceAudio = false);
    }

    public class ProxyUrlBuilder : IProxyUrlBuilder
    {
        public StreamingUrlDto? BuildProxyUrls(
            StreamingUrlDto? originalUrls,
            HttpRequest httpRequest,
            IMediaUrlCacheService cacheService,
            bool forceAudio = false)
        {
            if (originalUrls == null)
                return null;

            var baseUrl = $"{httpRequest.Scheme}://{httpRequest.Host}/api/stream/media";

            var proxyUrls = new StreamingUrlDto();

            // Determine if these are audio URLs
            // Check extensions OR if descriptions contain "audio" OR forced audio mode
            bool isAudio = forceAudio ||
                          IsAudioFormat(originalUrls.BestQualityFileExtension) ||
                          IsAudioFormat(originalUrls.MediumQualityFileExtension) ||
                          IsAudioFormat(originalUrls.WorstQualityFileExtension) ||
                          ContainsAudioIndicator(originalUrls.BestQualityDescription) ||
                          ContainsAudioIndicator(originalUrls.MediumQualityDescription) ||
                          ContainsAudioIndicator(originalUrls.WorstQualityDescription);

            // Build proxy URL for best quality
            if (!string.IsNullOrEmpty(originalUrls.BestQuality))
            {
                var cacheKey = cacheService.CacheSingleUrl(originalUrls.BestQuality, isAudio);
                proxyUrls.BestQuality = $"{baseUrl}/{cacheKey}";
                proxyUrls.BestQualityDescription = originalUrls.BestQualityDescription;
                // Standardize file extension to mp3 for audio, mp4 for video
                proxyUrls.BestQualityFileExtension = isAudio ? ".mp3" : ".mp4";
            }

            // Build proxy URL for medium quality
            if (!string.IsNullOrEmpty(originalUrls.MediumQuality))
            {
                var cacheKey = cacheService.CacheSingleUrl(originalUrls.MediumQuality, isAudio);
                proxyUrls.MediumQuality = $"{baseUrl}/{cacheKey}";
                proxyUrls.MediumQualityDescription = originalUrls.MediumQualityDescription;
                // Standardize file extension to mp3 for audio, mp4 for video
                proxyUrls.MediumQualityFileExtension = isAudio ? ".mp3" : ".mp4";
            }

            // Build proxy URL for worst quality
            if (!string.IsNullOrEmpty(originalUrls.WorstQuality))
            {
                var cacheKey = cacheService.CacheSingleUrl(originalUrls.WorstQuality, isAudio);
                proxyUrls.WorstQuality = $"{baseUrl}/{cacheKey}";
                proxyUrls.WorstQualityDescription = originalUrls.WorstQualityDescription;
                // Standardize file extension to mp3 for audio, mp4 for video
                proxyUrls.WorstQualityFileExtension = isAudio ? ".mp3" : ".mp4";
            }

            return proxyUrls;
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