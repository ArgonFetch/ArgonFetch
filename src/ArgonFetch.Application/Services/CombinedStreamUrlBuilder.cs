using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using Microsoft.AspNetCore.Http;

namespace ArgonFetch.Application.Services
{
    public interface ICombinedStreamUrlBuilder
    {
        StreamReferenceDto? BuildCombinedReferences(
            StreamingUrlDto? videoUrls,
            StreamingUrlDto? audioUrls,
            IMediaUrlCacheService cacheService,
            string? proxy = null);
    }

    public class CombinedStreamUrlBuilder : ICombinedStreamUrlBuilder
    {
        public StreamReferenceDto? BuildCombinedReferences(
            StreamingUrlDto? videoUrls,
            StreamingUrlDto? audioUrls,
            IMediaUrlCacheService cacheService,
            string? proxy = null)
        {
            if (videoUrls == null || audioUrls == null)
                return null;

            var combinedReferences = new StreamReferenceDto
            {
                UrlType = UrlType.Combined
            };

            // Build best quality reference with cache key
            if (!string.IsNullOrEmpty(videoUrls.BestQuality) && !string.IsNullOrEmpty(audioUrls.BestQuality))
            {
                var cacheKey = cacheService.CacheMediaUrls(videoUrls.BestQuality, audioUrls.BestQuality, proxy);
                combinedReferences.BestQualityKey = cacheKey;
                combinedReferences.BestQualityDescription = $"Combined: {videoUrls.BestQualityDescription} + {audioUrls.BestQualityDescription}";
                combinedReferences.BestQualityFileExtension = ".mp4"; // Combined streams are always MP4
                combinedReferences.BestQualityMimeType = "video/mp4";
            }

            // Build medium quality reference with cache key
            if (!string.IsNullOrEmpty(videoUrls.MediumQuality) && !string.IsNullOrEmpty(audioUrls.MediumQuality))
            {
                var cacheKey = cacheService.CacheMediaUrls(videoUrls.MediumQuality, audioUrls.MediumQuality, proxy);
                combinedReferences.MediumQualityKey = cacheKey;
                combinedReferences.MediumQualityDescription = $"Combined: {videoUrls.MediumQualityDescription} + {audioUrls.MediumQualityDescription}";
                combinedReferences.MediumQualityFileExtension = ".mp4"; // Combined streams are always MP4
                combinedReferences.MediumQualityMimeType = "video/mp4";
            }

            // Build worst quality reference with cache key
            if (!string.IsNullOrEmpty(videoUrls.WorstQuality) && !string.IsNullOrEmpty(audioUrls.WorstQuality))
            {
                var cacheKey = cacheService.CacheMediaUrls(videoUrls.WorstQuality, audioUrls.WorstQuality, proxy);
                combinedReferences.WorstQualityKey = cacheKey;
                combinedReferences.WorstQualityDescription = $"Combined: {videoUrls.WorstQualityDescription} + {audioUrls.WorstQualityDescription}";
                combinedReferences.WorstQualityFileExtension = ".mp4"; // Combined streams are always MP4
                combinedReferences.WorstQualityMimeType = "video/mp4";
            }

            return combinedReferences;
        }
    }
}