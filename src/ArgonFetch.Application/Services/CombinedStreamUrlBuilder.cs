using ArgonFetch.Application.Dtos;
using Microsoft.AspNetCore.Http;

namespace ArgonFetch.Application.Services
{
    public interface ICombinedStreamUrlBuilder
    {
        StreamingUrlDto? BuildCombinedUrls(
            StreamingUrlDto? videoUrls,
            StreamingUrlDto? audioUrls,
            HttpRequest httpRequest,
            IMediaUrlCacheService cacheService);
    }

    public class CombinedStreamUrlBuilder : ICombinedStreamUrlBuilder
    {
        public StreamingUrlDto? BuildCombinedUrls(
            StreamingUrlDto? videoUrls,
            StreamingUrlDto? audioUrls,
            HttpRequest httpRequest,
            IMediaUrlCacheService cacheService)
        {
            if (videoUrls == null || audioUrls == null)
                return null;

            var baseUrl = $"{httpRequest.Scheme}://{httpRequest.Host}/api/stream/combined";

            var combinedUrls = new StreamingUrlDto();

            // Build best quality URL with cache key
            if (!string.IsNullOrEmpty(videoUrls.BestQuality) && !string.IsNullOrEmpty(audioUrls.BestQuality))
            {
                var cacheKey = cacheService.CacheMediaUrls(videoUrls.BestQuality, audioUrls.BestQuality);
                combinedUrls.BestQuality = $"{baseUrl}/{cacheKey}";
                combinedUrls.BestQualityDescription = $"Combined: {videoUrls.BestQualityDescription} + {audioUrls.BestQualityDescription}";
                combinedUrls.BestQualityFileExtension = ".mp4"; // Combined streams are always MP4
            }

            // Build medium quality URL with cache key
            if (!string.IsNullOrEmpty(videoUrls.MediumQuality) && !string.IsNullOrEmpty(audioUrls.MediumQuality))
            {
                var cacheKey = cacheService.CacheMediaUrls(videoUrls.MediumQuality, audioUrls.MediumQuality);
                combinedUrls.MediumQuality = $"{baseUrl}/{cacheKey}";
                combinedUrls.MediumQualityDescription = $"Combined: {videoUrls.MediumQualityDescription} + {audioUrls.MediumQualityDescription}";
                combinedUrls.MediumQualityFileExtension = ".mp4"; // Combined streams are always MP4
            }

            // Build worst quality URL with cache key
            if (!string.IsNullOrEmpty(videoUrls.WorstQuality) && !string.IsNullOrEmpty(audioUrls.WorstQuality))
            {
                var cacheKey = cacheService.CacheMediaUrls(videoUrls.WorstQuality, audioUrls.WorstQuality);
                combinedUrls.WorstQuality = $"{baseUrl}/{cacheKey}";
                combinedUrls.WorstQualityDescription = $"Combined: {videoUrls.WorstQualityDescription} + {audioUrls.WorstQualityDescription}";
                combinedUrls.WorstQualityFileExtension = ".mp4"; // Combined streams are always MP4
            }

            return combinedUrls;
        }
    }
}