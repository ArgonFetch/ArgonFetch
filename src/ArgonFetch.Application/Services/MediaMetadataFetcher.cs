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

        /// <summary>
        /// Clients to ask as when YouTube refuses the ordinary one.
        /// </summary>
        /// <remarks>
        /// YouTube demands a proof-of-origin token from its web clients when the request comes
        /// from an address it does not trust - which is every server - and answers "sign in to
        /// confirm you're not a bot" when it does not get one. Its other clients are not asked
        /// for that token, so asking as one of them gets an answer where the default cannot.
        ///
        /// Which clients work is YouTube's to decide and it changes, so this is configuration
        /// rather than a constant: an instance can be pointed at a working one without waiting
        /// for a release.
        /// </remarks>
        public static readonly string[] DefaultPlayerClients = ["android_vr", "tv", "ios"];

        private readonly YoutubeDL _youtubeDL;
        private readonly IProxyPool _proxyPool;
        private readonly IToolPaths _toolPaths;
        private readonly string[] _playerClients;

        public MediaMetadataFetcher(
            YoutubeDL youtubeDL,
            IProxyPool proxyPool,
            IToolPaths toolPaths,
            string[]? playerClients = null)
        {
            _youtubeDL = youtubeDL;
            _proxyPool = proxyPool;
            _toolPaths = toolPaths;
            _playerClients = playerClients ?? DefaultPlayerClients;
        }

        /// <summary>Reads the configured list, falling back to the built-in one.</summary>
        public static string[] ReadPlayerClients(string? configured) =>
            string.IsNullOrWhiteSpace(configured)
                ? DefaultPlayerClients
                : configured
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();

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

            // The ordinary request was refused for wanting a session. Before giving up, ask as a
            // client YouTube does not demand a token from: one more call, and it often succeeds
            // where there is no cookies file to offer.
            if (!result.Success && YtDlpErrors.NeedsSignedInSession(result.ErrorOutput))
            {
                foreach (var client in _playerClients)
                {
                    options.ExtractorArgs = $"youtube:player_client={client}";
                    options.Proxy = proxy;

                    result = await _youtubeDL.RunVideoDataFetch(query, overrideOptions: options);

                    if (result.Success)
                        return new MediaMetadata(result.Data, proxy);
                }

                options.ExtractorArgs = null;
            }

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
