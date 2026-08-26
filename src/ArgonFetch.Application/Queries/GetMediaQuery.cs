using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Services;
using MediaTags = ArgonFetch.Application.Services.MediaTags;
using ArgonFetch.Abstractions;
using ArgonFetch.Application.Plugins;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace ArgonFetch.Application.Queries
{
    public class GetMediaQuery : IRequest<ResourceInformationDto>
    {
        public GetMediaQuery(string url)
        {
            Query = url;
        }

        public string Query { get; set; }
    }

    public class GetMediaQueryHandler : IRequestHandler<GetMediaQuery, ResourceInformationDto>
    {
        private readonly YoutubeDL _youtubeDL;
        private readonly IMemoryCache _memoryCache;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICombinedStreamUrlBuilder _combinedUrlBuilder;
        private readonly IMediaUrlCacheService _cacheService;
        private readonly IProxyUrlBuilder _proxyUrlBuilder;
        private readonly IProxyPool _proxyPool;
        private readonly IToolPaths _toolPaths;
        private readonly IProviderRegistry _providers;
        private readonly IProviderContextFactory _providerContexts;
        private readonly ILogger<GetMediaQueryHandler> _logger;

        // Media URLs are signed for the IP that asked, so the proxy travels with them.
        private string? _fetchProxy;

        private MediaTags? _overrideTags;
        private string? _overrideCover;
        private string? _originalUrl;

        public GetMediaQueryHandler(
            YoutubeDL youtubeDL,
            IMemoryCache memoryCache,
            IHttpContextAccessor httpContextAccessor,
            ICombinedStreamUrlBuilder combinedUrlBuilder,
            IMediaUrlCacheService cacheService,
            IProxyUrlBuilder proxyUrlBuilder,
            IProxyPool proxyPool,
            IToolPaths toolPaths,
            IProviderRegistry providers,
            IProviderContextFactory providerContexts,
            ILogger<GetMediaQueryHandler> logger
            )
        {
            _youtubeDL = youtubeDL;
            _memoryCache = memoryCache;
            _httpContextAccessor = httpContextAccessor;
            _combinedUrlBuilder = combinedUrlBuilder;
            _cacheService = cacheService;
            _proxyUrlBuilder = proxyUrlBuilder;
            _proxyPool = proxyPool;
            _toolPaths = toolPaths;
            _providers = providers;
            _providerContexts = providerContexts;
            _logger = logger;
        }

        public async ValueTask<ResourceInformationDto> Handle(GetMediaQuery request, CancellationToken cancellationToken)
        {
            var resolved = await Resolve(request, cancellationToken);

            resolved.RequestedUrl = request.Query;

            return resolved;
        }

        private async Task<ResourceInformationDto> Resolve(GetMediaQuery request, CancellationToken cancellationToken)
        {
            if (Uri.TryCreate(request.Query, UriKind.Absolute, out var link))
            {
                var handled = await AskProvidersAsync(link, request, cancellationToken);

                if (handled is not null)
                    return handled;
            }

            var platform = PlatformIdentifierService.IdentifyPlatform(request.Query);

            if (await IsCollection(request.Query, platform))
                return await HandleCollection(request.Query);

            var resultData = await YT_DLP_Fetch(request.Query);

            if (resultData.ResultType == MetadataType.Video)
            {
                string thumbnailUrl = resultData.Thumbnail;

                if (resultData.Thumbnails?.Any() == true)
                {
                    var squareThumbnails = resultData.Thumbnails
                        .Where(t => t.Width == t.Height && t.Width.HasValue)
                        .ToList();

                    if (squareThumbnails.Any())
                    {
                        thumbnailUrl = squareThumbnails
                            .OrderByDescending(t => t.Width)
                            .First()
                            .Url;
                    }
                }

                var combinedFormats = ExtractCombinedFormatsAndCacheNewUrl(resultData.Formats);

                StreamReferenceDto? combinedReferences = null;
                StreamReferenceDto? audioReferences = null;

                var tags = _overrideTags ?? new MediaTags(resultData.Title, resultData.Uploader);

                var audioRenditions = _proxyUrlBuilder.BuildRenditions(
                    RenditionPicker.PickAudio(AudioSources(resultData.Formats)),
                    _cacheService,
                    isAudio: true,
                    proxy: _fetchProxy,
                    tags: tags);

                List<MediaRenditionDto> videoRenditions;
                UrlType videoUrlType;

                if (HasValidUrls(combinedFormats))
                {
                    videoUrlType = UrlType.Media;
                    videoRenditions = _proxyUrlBuilder.BuildRenditions(
                        RenditionPicker.PickVideo(PreMuxedSources(resultData.Formats), perContainer: true),
                        _cacheService,
                        isAudio: false,
                        proxy: _fetchProxy,
                        tags: tags);
                }
                else
                {
                    videoUrlType = UrlType.Combined;
                    videoRenditions = _combinedUrlBuilder.BuildCombinedRenditions(
                        RenditionPicker.PickVideo(VideoOnlySources(resultData.Formats)),
                        RenditionPicker.PickAudio(AudioSources(resultData.Formats), count: 1).FirstOrDefault(),
                        _cacheService,
                        _fetchProxy,
                        tags);
                }

                combinedReferences = videoRenditions.Count > 0
                    ? new StreamReferenceDto { UrlType = videoUrlType, Renditions = videoRenditions }
                    : null;

                audioReferences = audioRenditions.Count > 0
                    ? new StreamReferenceDto { UrlType = UrlType.Media, Renditions = audioRenditions }
                    : null;

                return new ResourceInformationDto
                {
                    Type = MediaType.Media,
                    MediaItems =
                    [
                            new MediaInformationDto
                            {
                                RequestedUrl = _originalUrl ?? request.Query,
                                Video = combinedReferences,  // Either pre-muxed or FFmpeg-combined
                                Audio = audioReferences,      // Audio-only option
                                CoverUrl = _overrideCover ?? thumbnailUrl,
                                Title = tags.Title ?? string.Empty,
                                Author = tags.Artist ?? string.Empty
                            }
                    ]
                };
            }
            else
                throw new NotSupportedException("This isn't implemented yet");
        }

        private async Task<ResourceInformationDto?> AskProvidersAsync(
            Uri link,
            GetMediaQuery request,
            CancellationToken cancellationToken)
        {
            var provider = _providers.For(link);

            if (provider is null)
                return null;

            ProviderOutcome outcome;

            try
            {
                var context = _providerContexts.For(provider.Id, ProbeAsync);

                outcome = await provider.PrepareAsync(link, context, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "The {Id} plugin failed on {Url}; falling back", provider.Id, link);
                return null;
            }

            switch (outcome)
            {
                case ProviderOutcome.RewriteOutcome rewrite:
                    _logger.LogInformation("The {Id} plugin redirected {From} to {To}", provider.Id, link, rewrite.Url);

                    _overrideTags = new MediaTags(rewrite.Tags.Title, rewrite.Tags.Artist);
                    _overrideCover = rewrite.CoverUrl;
                    _originalUrl = request.Query;
                    request.Query = rewrite.Url.ToString();

                    return null;

                case ProviderOutcome.ListingOutcome listing:
                    return MapCollection(listing.Collection, provider.Id);

                case ProviderOutcome.CompleteOutcome complete:
                    return MapMedia(complete.Media, request.Query);

                default:
                    return null;
            }
        }

        private ResourceInformationDto MapCollection(CollectionResult collection, string pluginId)
        {
            if (collection.MayBeTruncated)
            {
                _logger.LogInformation(
                    "The {Id} plugin returned {Count} entries and says there may be more it could not send",
                    pluginId, collection.Items.Count);
            }

            return new ResourceInformationDto
            {
                Type = MediaType.PlayList,
                Title = collection.Title,
                Author = collection.Author,
                CoverUrl = collection.CoverUrl,
                MediaItems = collection.Items
                    .Select(item => new MediaInformationDto
                    {
                        RequestedUrl = item.Url.ToString(),
                        Title = item.Title,
                        Author = item.Author ?? string.Empty,
                        CoverUrl = item.CoverUrl ?? collection.CoverUrl,
                        Audio = null,
                        Video = null
                    })
                    .ToList()
            };
        }

        private ResourceInformationDto MapMedia(MediaResult media, string requestedUrl)
        {
            var tags = new MediaTags(media.Title, media.Author);

            var audio = BuildReference(media.Streams.Where(stream => stream.IsAudio), isAudio: true, tags);
            var video = BuildReference(media.Streams.Where(stream => !stream.IsAudio), isAudio: false, tags);

            return new ResourceInformationDto
            {
                Type = MediaType.Media,
                MediaItems =
                [
                    new MediaInformationDto
                    {
                        RequestedUrl = requestedUrl,
                        Video = video,
                        Audio = audio,
                        CoverUrl = media.CoverUrl,
                        Title = media.Title,
                        Author = media.Author ?? string.Empty
                    }
                ]
            };
        }

        private StreamReferenceDto? BuildReference(IEnumerable<MediaStream> streams, bool isAudio, MediaTags tags)
        {
            var renditions = streams
                .Select(stream => new MediaRenditionDto
                {
                    Key = _cacheService.CacheSingleUrl(
                        stream.Url.ToString(),
                        isAudio,
                        stream.MimeType,
                        stream.Proxy,
                        tags),
                    Label = stream.Label ?? (isAudio ? "Audio" : "Video"),
                    FileExtension = stream.FileExtension
                        ?? MediaFormats.ExtensionFor(stream.MimeType)
                        ?? (isAudio ? ".m4a" : ".mp4"),
                    MimeType = stream.MimeType ?? string.Empty,
                    FileSizeBytes = stream.SizeBytes,
                    UrlType = UrlType.Media
                })
                .ToList();

            return renditions.Count == 0
                ? null
                : new StreamReferenceDto { UrlType = UrlType.Media, Renditions = renditions };
        }

        private async Task<ProbeResult?> ProbeAsync(Uri url, CancellationToken cancellationToken)
        {
            try
            {
                var fetched = await YT_DLP_Fetch(url.ToString());

                return new ProbeResult(fetched.Title, fetched.Uploader, fetched.Duration);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not probe {Url}", url);
                return null;
            }
        }

        private static async Task<bool> IsCollection(string query, Platform platform)
        {
            try
            {
                return await MediaContentIdentifierService.IdentifyContent(query, platform) == ContentType.Playlist;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task<ResourceInformationDto> HandleCollection(string query)
        {
            var listing = await YT_DLP_Fetch(query, new OptionSet
            {
                DumpSingleJson = true,
                FlatPlaylist = true,
            });

            var entries = listing.Entries ?? [];

            _logger.LogInformation("Listed {Count} entries for {Url}", entries.Length, query);

            return new ResourceInformationDto
            {
                Type = MediaType.PlayList,
                Title = listing.Title,
                Author = listing.Uploader ?? listing.Channel,
                CoverUrl = LargestThumbnail(listing),
                MediaItems = entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Url) || !string.IsNullOrWhiteSpace(entry.WebpageUrl))
                    .Select(entry => new MediaInformationDto
                    {
                        RequestedUrl = entry.WebpageUrl ?? entry.Url,
                        Title = entry.Title ?? NameFromUrl(entry.WebpageUrl ?? entry.Url) ?? "Unknown",
                        Author = entry.Uploader ?? entry.Channel ?? listing.Uploader ?? string.Empty,
                        CoverUrl = LargestThumbnail(entry) ?? LargestThumbnail(listing),
                    })
                    .ToList()
            };
        }

        internal static string? NameFromUrl(string? url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return null;

            var slug = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault();

            if (string.IsNullOrWhiteSpace(slug))
                return null;

            var words = slug
                .Replace('-', ' ')
                .Replace('_', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..]);

            var name = string.Join(" ", words);

            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        private static string? LargestThumbnail(VideoData data)
        {
            var largest = data.Thumbnails?
                .Where(thumbnail => !string.IsNullOrWhiteSpace(thumbnail.Url))
                .OrderByDescending(thumbnail => (long)(thumbnail.Width ?? 0) * (thumbnail.Height ?? 0))
                .FirstOrDefault();

            return largest?.Url ?? (string.IsNullOrWhiteSpace(data.Thumbnail) ? null : data.Thumbnail);
        }

        private StreamingUrlDto ExtractCombinedFormatsAndCacheNewUrl(FormatData[] formatData)
        {
            var combinedFormats = formatData
                .Where(f =>
                    !string.IsNullOrEmpty(f.VideoCodec) &&
                    !string.IsNullOrEmpty(f.AudioCodec) &&
                    f.VideoCodec != "none" &&
                    f.AudioCodec != "none" &&
                    !f.Protocol.Contains("mhtml") &&
                    !f.Protocol.Contains("m3u8") &&
                    (f.Extension?.Equals(".mp4", StringComparison.OrdinalIgnoreCase) == true ||
                     f.Extension?.Equals(".webm", StringComparison.OrdinalIgnoreCase) == true)
                )
                .OrderByDescending(f => f.Height ?? 0)
                .ThenByDescending(f => f.Bitrate)
                .ToList();

            if (combinedFormats.Any())
            {
                var bestVideo = combinedFormats.FirstOrDefault();
                var mediumVideo = combinedFormats.ElementAtOrDefault(combinedFormats.Count() / 2);
                var worstVideo = combinedFormats.LastOrDefault();

                return new StreamingUrlDto
                {
                    BestQualityDescription = bestVideo?.Format,
                    BestQuality = bestVideo?.Url,
                    BestQualityFileExtension = bestVideo?.Extension,

                    MediumQualityDescription = mediumVideo?.Format,
                    MediumQuality = mediumVideo?.Url,
                    MediumQualityFileExtension = mediumVideo?.Extension,

                    WorstQualityDescription = worstVideo?.Format,
                    WorstQuality = worstVideo?.Url,
                    WorstQualityFileExtension = worstVideo?.Extension,
                };
            }

            return new StreamingUrlDto();
        }

        private bool HasValidUrls(StreamingUrlDto? urls)
        {
            return urls != null &&
                   (!string.IsNullOrEmpty(urls.BestQuality) ||
                    !string.IsNullOrEmpty(urls.MediumQuality) ||
                    !string.IsNullOrEmpty(urls.WorstQuality));
        }

        private static IEnumerable<RenditionSource> PreMuxedSources(FormatData[] formatData) =>
            formatData
                .Where(f =>
                    !string.IsNullOrEmpty(f.Url) &&
                    !string.IsNullOrEmpty(f.VideoCodec) && f.VideoCodec != "none" &&
                    !string.IsNullOrEmpty(f.AudioCodec) && f.AudioCodec != "none" &&
                    !f.Protocol.Contains("mhtml") &&
                    !f.Protocol.Contains("m3u8"))
                .Select(ToSource);

        private static IEnumerable<RenditionSource> VideoOnlySources(FormatData[] formatData) =>
            formatData
                .Where(f =>
                    !string.IsNullOrEmpty(f.Url) &&
                    !string.IsNullOrEmpty(f.VideoCodec) && f.VideoCodec != "none" &&
                    (string.IsNullOrEmpty(f.AudioCodec) || f.AudioCodec == "none") &&
                    !f.Protocol.Contains("mhtml") &&
                    !f.Protocol.Contains("m3u8"))
                .Select(ToSource);

        private static IEnumerable<RenditionSource> AudioSources(FormatData[] formatData) =>
            formatData
                .Where(f =>
                    !string.IsNullOrEmpty(f.Url) &&
                    !string.IsNullOrEmpty(f.AudioCodec) && f.AudioCodec != "none" &&
                    (string.IsNullOrEmpty(f.VideoCodec) || f.VideoCodec == "none") &&
                    !f.Protocol.Contains("mhtml") &&
                    !f.Protocol.Contains("m3u8") &&
                    f.AudioBitrate is > 0)
                .OrderByDescending(f => f.Bitrate * OpusQualityFactor(f.AudioCodec))
                .Select(ToSource);

        private static RenditionSource ToSource(FormatData format) => new(
            format.Url,
            format.Format,
            format.Extension,
            format.Height,
            format.Bitrate,
            (long?)(format.FileSize ?? format.ApproximateFileSize));

        private static string WatchUrl(string id) => $"https://music.youtube.com/watch?v={id}";

        private static double OpusQualityFactor(string? audioCodec) =>
            audioCodec?.StartsWith("opus", StringComparison.OrdinalIgnoreCase) == true ? 1.2 : 1.0;

        private async Task<VideoData> YT_DLP_Fetch(string query, OptionSet? options = null)
        {
            options ??= new OptionSet { DumpSingleJson = true };

            options.Cookies = _toolPaths.CookiesPath;

            if (!Uri.IsWellFormedUriString(query, UriKind.Absolute))
            {
                _fetchProxy = _proxyPool.Next();

                var searchOptions = new OptionSet
                {
                    NoPlaylist = true,
                    Proxy = _fetchProxy,
                    Cookies = _toolPaths.CookiesPath,
                };

                var searchResult = await _youtubeDL.RunVideoDataFetch($"ytsearch:{query}", overrideOptions: searchOptions);
                query = searchResult.Data.Entries.First().Url;
            }

            var attempts = Math.Min(Math.Max(_proxyPool.Count, 1), 3);
            RunResult<VideoData> result;

            do
            {
                _fetchProxy = _proxyPool.Next();
                options.Proxy = _fetchProxy;
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

            return result.Data;
        }

    }
}
