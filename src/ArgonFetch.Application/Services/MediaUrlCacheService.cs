using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace ArgonFetch.Application.Services
{
    public interface IMediaUrlCacheService
    {
        string CacheMediaUrls(string videoUrl, string audioUrl, TimeSpan? expiration = null);
        (string? videoUrl, string? audioUrl) GetCachedUrls(string cacheKey);
        string CacheSingleUrl(string url, TimeSpan? expiration = null);
        string CacheSingleUrl(string url, bool isAudio, TimeSpan? expiration = null);
        string? GetCachedSingleUrl(string cacheKey);
        (string Url, bool IsAudio)? GetCachedUrlWithFormat(string cacheKey);
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

        public string CacheMediaUrls(string videoUrl, string audioUrl, TimeSpan? expiration = null)
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
                CachedAt = DateTime.UtcNow
            };

            _cache.Set(CACHE_PREFIX + cacheKey, cacheData, cacheOptions);

            return cacheKey;
        }

        public (string? videoUrl, string? audioUrl) GetCachedUrls(string cacheKey)
        {
            if (_cache.TryGetValue(CACHE_PREFIX + cacheKey, out CachedMediaUrls? cachedData) && cachedData != null)
            {
                return (cachedData.VideoUrl, cachedData.AudioUrl);
            }

            return (null, null);
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

        public string CacheSingleUrl(string url, bool isAudio, TimeSpan? expiration = null)
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

        public (string Url, bool IsAudio)? GetCachedUrlWithFormat(string cacheKey)
        {
            if (_cache.TryGetValue(CACHE_PREFIX + cacheKey, out object? cachedData))
            {
                if (cachedData is CachedSingleUrl singleUrl)
                {
                    return (singleUrl.Url, singleUrl.IsAudio);
                }
                else if (cachedData is string url)
                {
                    // Legacy format - assume video (since we don't know)
                    return (url, false);
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
            public DateTime CachedAt { get; set; }
        }

        private class CachedSingleUrl
        {
            public required string Url { get; set; }
            public required bool IsAudio { get; set; }
            public DateTime CachedAt { get; set; }
        }
    }
}