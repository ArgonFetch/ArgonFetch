using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using Microsoft.AspNetCore.Http;

namespace ArgonFetch.Application.Services
{
    public interface ICombinedStreamUrlBuilder
    {
        List<MediaRenditionDto> BuildCombinedRenditions(
            IEnumerable<RenditionSource> videoSources,
            RenditionSource? audioSource,
            IMediaUrlCacheService cacheService,
            string? proxy = null,
            MediaTags? tags = null);
    }

    public class CombinedStreamUrlBuilder : ICombinedStreamUrlBuilder
    {
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