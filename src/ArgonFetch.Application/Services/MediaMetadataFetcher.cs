using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace ArgonFetch.Application.Services
{
    /// <summary>What yt-dlp answered, and the proxy it answered through.</summary>
    /// <remarks>
    /// The proxy is part of the result because media URLs are signed for the address that asked
    /// for them. Fetch through one exit and stream through another and the source returns 403.
    /// </remarks>
    public record MediaMetadata(VideoData Data, string? Proxy);

    public interface IMediaMetadataFetcher
    {
        Task<MediaMetadata> FetchAsync(string query, OptionSet? options = null);
    }

    public class MediaMetadataFetcher : IMediaMetadataFetcher
    {
        private const int MaxAttempts = 3;

        // Well inside the hours a signed media URL lives.
        private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(20);

        private readonly YoutubeDL _youtubeDL;
        private readonly IProxyPool _proxyPool;
        private readonly IToolPaths _toolPaths;
        private readonly IMemoryCache _cache;
        private readonly YtDlpWorker _worker;

        public MediaMetadataFetcher(YoutubeDL youtubeDL, IProxyPool proxyPool, IToolPaths toolPaths, IMemoryCache cache, YtDlpWorker worker)
        {
            _youtubeDL = youtubeDL;
            _proxyPool = proxyPool;
            _toolPaths = toolPaths;
            _cache = cache;
            _worker = worker;
        }

        // The worker understands only what this class sets: a proxy, cookies and a flat listing.
        private async Task<RunResult<VideoData>> RunOnceAsync(string query, OptionSet options)
        {
            try
            {
                var json = await _worker.TryFetchJsonAsync(query, options.Proxy, options.Cookies, options.FlatPlaylist);

                if (json is not null)
                    return new RunResult<VideoData>(true, [], JsonConvert.DeserializeObject<VideoData>(json)!);
            }
            catch (YtDlpFailure failure)
            {
                return new RunResult<VideoData>(false, [failure.Message], null!);
            }

            return await _youtubeDL.RunVideoDataFetch(query, overrideOptions: options);
        }

        public Task<MediaMetadata> FetchAsync(string query, OptionSet? options = null)
        {
            // Only the plain lookup is shared; a caller with options of its own wants its own run.
            if (options is not null)
                return RunAsync(query, options);

            // A plugin probing a match and the fetch that follows ask for the same URL, seconds apart.
            // ponytail: two first askers can race into two runs - wrap in Lazy if that shows up.
            var key = $"yt-dlp:{query}";

            if (_cache.TryGetValue(key, out Task<MediaMetadata>? pending) && pending is not null)
                return pending;

            var task = RunAsync(query, new OptionSet { DumpSingleJson = true });
            _cache.Set(key, task, Fresh);

            // A failure is not an answer worth keeping.
            task.ContinueWith(_ => _cache.Remove(key), TaskContinuationOptions.NotOnRanToCompletion);

            return task;
        }

        private async Task<MediaMetadata> RunAsync(string query, OptionSet options)
        {

            options.Cookies = _toolPaths.CookiesPath;

            string? proxy;

            if (!Uri.IsWellFormedUriString(query, UriKind.Absolute))
            {
                proxy = _proxyPool.Next();

                var searchOptions = new OptionSet
                {
                    NoPlaylist = true,
                    Proxy = proxy,
                    Cookies = _toolPaths.CookiesPath,
                };

                var searchResult = await _youtubeDL.RunVideoDataFetch($"ytsearch:{query}", overrideOptions: searchOptions);
                query = searchResult.Data.Entries.First().Url;
            }

            var attempts = Math.Min(Math.Max(_proxyPool.Count, 1), MaxAttempts);
            RunResult<VideoData> result;

            do
            {
                proxy = _proxyPool.Next();
                options.Proxy = proxy;
                result = await RunOnceAsync(query, options);
            }
            while (!result.Success && --attempts > 0);

            if (!result.Success)
            {
                var errors = string.Join(", ", result.ErrorOutput);

                if (YtDlpErrors.IsDrmProtected(result.ErrorOutput))
                    throw new NotSupportedException("This media is DRM protected and cannot be downloaded.");

                if (YtDlpErrors.NeedsSignedInSession(result.ErrorOutput))
                    throw new NotSupportedException(
                        _toolPaths.CookiesPath is null
                            ? "This source serves media only to a signed-in session. Set COOKIES_PATH to a Netscape-format cookies file exported from a logged-in browser."
                            : "This source rejected the configured session. The cookies file may have expired.");

                throw new ArgumentException($"Failed to fetch data: {errors}");
            }

            return new MediaMetadata(result.Data, proxy);
        }
    }
}
