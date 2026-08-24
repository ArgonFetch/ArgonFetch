using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace ArgonFetch.Application.Services
{
    public interface IMediaUrlCacheService
    {
        string CacheMediaUrls(string videoUrl, string audioUrl, string? proxy = null, MediaTags? tags = null, TimeSpan? expiration = null);
        (string? videoUrl, string? audioUrl, string? proxy, MediaTags tags) GetCachedUrls(string cacheKey);
        string CacheSingleUrl(string url, TimeSpan? expiration = null);
        string CacheSingleUrl(string url, bool isAudio, string? mimeType = null, string? proxy = null, MediaTags? tags = null, TimeSpan? expiration = null);
        string? GetCachedSingleUrl(string cacheKey);
        (string Url, bool IsAudio, string? MimeType, string? Proxy, MediaTags Tags)? GetCachedUrlWithFormat(string cacheKey);
        void RemoveFromCache(string cacheKey);
    }

    public class MediaUrlCacheService : IMediaUrlCacheService
    {
        private readonly IMemoryCache _cache;
        private const string CACHE_PREFIX = "media_urls_";

        public MediaUrlCacheService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public string CacheMediaUrls(string videoUrl, string audioUrl, string? proxy = null, MediaTags? tags = null, TimeSpan? expiration = null)
        {
            // Generate a unique cache key
            var cacheKey = GenerateCacheKey(videoUrl, audioUrl);

            // Store URLs in cache with expiration (default 1 hour)
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromHours(1)
            };

            var cacheData = new CachedMediaUrls
            {
                VideoUrl = videoUrl,
                AudioUrl = audioUrl,
                Proxy = proxy,
                Tags = tags ?? MediaTags.None,
                CachedAt = DateTime.UtcNow
            };

            _cache.Set(CACHE_PREFIX + cacheKey, cacheData, cacheOptions);

            return cacheKey;
        }

        public (string? videoUrl, string? audioUrl, string? proxy, MediaTags tags) GetCachedUrls(string cacheKey)
        {
            if (_cache.TryGetValue(CACHE_PREFIX + cacheKey, out CachedMediaUrls? cachedData) && cachedData != null)
            {
                return (cachedData.VideoUrl, cachedData.AudioUrl, cachedData.Proxy, cachedData.Tags);
            }

            return (null, null, null, MediaTags.None);
        }

        public string CacheSingleUrl(string url, TimeSpan? expiration = null)
        {
            // Generate a unique cache key for single URL
            var cacheKey = GenerateSingleUrlCacheKey(url);

            // Store URL in cache with expiration (default 1 hour)
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromHours(1)
            };

            _cache.Set(CACHE_PREFIX + cacheKey, url, cacheOptions);

            return cacheKey;
        }

        public string CacheSingleUrl(string url, bool isAudio, string? mimeType = null, string? proxy = null, MediaTags? tags = null, TimeSpan? expiration = null)
        {
            // Generate a unique cache key for single URL
            var cacheKey = GenerateSingleUrlCacheKey(url);

            // Store URL with format info in cache with expiration (default 1 hour)
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromHours(1)
            };

            var cacheData = new CachedSingleUrl
            {
                Url = url,
                IsAudio = isAudio,
                // Carried so the stream endpoint serves exactly the format the fetch response
                // advertised, instead of re-deriving it from a URL that has no extension.
                MimeType = mimeType,
                // Media URLs are signed for the IP that requested them, so the download has to
                // leave through the same proxy the extraction did or the source answers 403.
                Proxy = proxy,
                // Kept with the url because by streaming time only a cache key is left, and a
                // file with no title is what the download otherwise lands as.
                Tags = tags ?? MediaTags.None,
                CachedAt = DateTime.UtcNow
            };

            _cache.Set(CACHE_PREFIX + cacheKey, cacheData, cacheOptions);

            return cacheKey;
        }

        public string? GetCachedSingleUrl(string cacheKey)
        {
            // Try to get as new format first
            if (_cache.TryGetValue(CACHE_PREFIX + cacheKey, out object? cachedData))
            {
                if (cachedData is CachedSingleUrl singleUrl)
                {
                    return singleUrl.Url;
                }
                else if (cachedData is string url)
                {
                    // Legacy format - just URL string
                    return url;
                }
            }

            return null;
        }

        public (string Url, bool IsAudio, string? MimeType, string? Proxy, MediaTags Tags)? GetCachedUrlWithFormat(string cacheKey)
        {
            if (_cache.TryGetValue(CACHE_PREFIX + cacheKey, out object? cachedData))
            {
                if (cachedData is CachedSingleUrl singleUrl)
                {
                    return (singleUrl.Url, singleUrl.IsAudio, singleUrl.MimeType, singleUrl.Proxy, singleUrl.Tags);
                }
                else if (cachedData is string url)
                {
                    // Legacy format - assume video (since we don't know)
                    return (url, false, null, null, MediaTags.None);
                }
            }

            return null;
        }

        public void RemoveFromCache(string cacheKey)
        {
            _cache.Remove(CACHE_PREFIX + cacheKey);
        }

        private string GenerateCacheKey(string videoUrl, string audioUrl)
        {
            // Create a unique key based on URLs
            var combined = $"{videoUrl}|{audioUrl}";
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));

            // Convert to URL-safe base64
            var base64 = Convert.ToBase64String(hashBytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "")
                .Substring(0, 16); // Use first 16 chars for shorter URLs

            return base64;
        }

        private string GenerateSingleUrlCacheKey(string url)
        {
            // Create a unique key based on single URL
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(url));

            // Convert to URL-safe base64
            var base64 = Convert.ToBase64String(hashBytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "")
                .Substring(0, 16); // Use first 16 chars for shorter URLs

            return base64;
        }

        private class CachedMediaUrls
        {
            public required string VideoUrl { get; set; }
            public required string AudioUrl { get; set; }
            public string? Proxy { get; set; }
            public MediaTags Tags { get; set; } = MediaTags.None;
            public DateTime CachedAt { get; set; }
        }

        private class CachedSingleUrl
        {
            public required string Url { get; set; }
            public required bool IsAudio { get; set; }
            public string? MimeType { get; set; }
            public string? Proxy { get; set; }
            public MediaTags Tags { get; set; } = MediaTags.None;
            public DateTime CachedAt { get; set; }
        }
    }
}