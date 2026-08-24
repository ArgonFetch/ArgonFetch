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
            string? proxy = null,
            MediaTags? tags = null);

        /// <summary>
        /// Pairs each video rendition with the given audio track for muxing, best first.
        /// </summary>
        List<MediaRenditionDto> BuildCombinedRenditions(
            IEnumerable<RenditionSource> videoSources,
            RenditionSource? audioSource,
            IMediaUrlCacheService cacheService,
            string? proxy = null,
            MediaTags? tags = null);
    }

    public class CombinedStreamUrlBuilder : ICombinedStreamUrlBuilder
    {
        public StreamReferenceDto? BuildCombinedReferences(
            StreamingUrlDto? videoUrls,
            StreamingUrlDto? audioUrls,
            IMediaUrlCacheService cacheService,
            string? proxy = null,
            MediaTags? tags = null)
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
                var cacheKey = cacheService.CacheMediaUrls(videoUrls.BestQuality, audioUrls.BestQuality, proxy, tags);
                combinedReferences.BestQualityKey = cacheKey;
                combinedReferences.BestQualityDescription = $"Combined: {videoUrls.BestQualityDescription} + {audioUrls.BestQualityDescription}";
                combinedReferences.BestQualityFileExtension = ".mp4"; // Combined streams are always MP4
                combinedReferences.BestQualityMimeType = "video/mp4";
            }

            // Build medium quality reference with cache key
            if (!string.IsNullOrEmpty(videoUrls.MediumQuality) && !string.IsNullOrEmpty(audioUrls.MediumQuality))
            {
                var cacheKey = cacheService.CacheMediaUrls(videoUrls.MediumQuality, audioUrls.MediumQuality, proxy, tags);
                combinedReferences.MediumQualityKey = cacheKey;
                combinedReferences.MediumQualityDescription = $"Combined: {videoUrls.MediumQualityDescription} + {audioUrls.MediumQualityDescription}";
                combinedReferences.MediumQualityFileExtension = ".mp4"; // Combined streams are always MP4
                combinedReferences.MediumQualityMimeType = "video/mp4";
            }

            // Build worst quality reference with cache key
            if (!string.IsNullOrEmpty(videoUrls.WorstQuality) && !string.IsNullOrEmpty(audioUrls.WorstQuality))
            {
                var cacheKey = cacheService.CacheMediaUrls(videoUrls.WorstQuality, audioUrls.WorstQuality, proxy, tags);
                combinedReferences.WorstQualityKey = cacheKey;
                combinedReferences.WorstQualityDescription = $"Combined: {videoUrls.WorstQualityDescription} + {audioUrls.WorstQualityDescription}";
                combinedReferences.WorstQualityFileExtension = ".mp4"; // Combined streams are always MP4
                combinedReferences.WorstQualityMimeType = "video/mp4";
            }

            return combinedReferences;
        }

        public List<MediaRenditionDto> BuildCombinedRenditions(
            IEnumerable<RenditionSource> videoSources,
            RenditionSource? audioSource,
            IMediaUrlCacheService cacheService,
            string? proxy = null,
            MediaTags? tags = null)
        {
            var renditions = new List<MediaRenditionDto>();

            if (audioSource == null)
                return renditions;

            foreach (var video in videoSources)
            {
                renditions.Add(new MediaRenditionDto
                {
                    Key = cacheService.CacheMediaUrls(video.Url, audioSource.Url, proxy, tags),
                    Label = RenditionPicker.Label(video, isAudio: false),
                    Description = $"{video.Description} + {audioSource.Description}",
                    FileExtension = ".mp4",
                    MimeType = "video/mp4",
                    UrlType = UrlType.Combined,
                    // Only reported when both halves report one: a partial sum would read as a
                    // small download and be wrong by however much the other track weighs.
                    FileSizeBytes = video.FileSizeBytes.HasValue && audioSource.FileSizeBytes.HasValue
                        ? video.FileSizeBytes + audioSource.FileSizeBytes
                        : null,
                    Height = video.Height,
                    Bitrate = video.Bitrate
                });
            }

            return renditions;
        }
    }
}