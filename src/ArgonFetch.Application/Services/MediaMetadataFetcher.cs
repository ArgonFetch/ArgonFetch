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

        private readonly YoutubeDL _youtubeDL;
        private readonly IProxyPool _proxyPool;
        private readonly IToolPaths _toolPaths;

        public MediaMetadataFetcher(YoutubeDL youtubeDL, IProxyPool proxyPool, IToolPaths toolPaths)
        {
            _youtubeDL = youtubeDL;
            _proxyPool = proxyPool;
            _toolPaths = toolPaths;
        }

        public async Task<MediaMetadata> FetchAsync(string query, OptionSet? options = null)
        {
            options ??= new OptionSet { DumpSingleJson = true };

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
                result = await _youtubeDL.RunVideoDataFetch(query, overrideOptions: options);
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
