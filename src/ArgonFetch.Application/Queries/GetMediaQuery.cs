using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Services;
using ArgonFetch.Application.Services.DDLFetcherServices;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
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
        private readonly ISpotifyMetadataService _spotifyMetadataService;
        private readonly YTMusicAPI.SearchClient _ytmSearchClient;
        private readonly TikTokDllFetcherService _tikTokDllFetcherService;
        private readonly IMemoryCache _memoryCache;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICombinedStreamUrlBuilder _combinedUrlBuilder;
        private readonly IMediaUrlCacheService _cacheService;
        private readonly IProxyUrlBuilder _proxyUrlBuilder;
        private readonly IProxyPool _proxyPool;

        // The proxy the last extraction went through. Media URLs are signed for the IP that
        // requested them, so it has to travel with them to the stream endpoint.
        private string? _fetchProxy;

        public GetMediaQueryHandler(
            ISpotifyMetadataService spotifyMetadataService,
            YTMusicAPI.SearchClient ytmSearchClient,
            YoutubeDL youtubeDL,
            TikTokDllFetcherService tikTokDllFetcherService,
            IMemoryCache memoryCache,
            IHttpContextAccessor httpContextAccessor,
            ICombinedStreamUrlBuilder combinedUrlBuilder,
            IMediaUrlCacheService cacheService,
            IProxyUrlBuilder proxyUrlBuilder,
            IProxyPool proxyPool
            )
        {
            _spotifyMetadataService = spotifyMetadataService;
            _ytmSearchClient = ytmSearchClient;
            _youtubeDL = youtubeDL;
            _tikTokDllFetcherService = tikTokDllFetcherService;
            _memoryCache = memoryCache;
            _httpContextAccessor = httpContextAccessor;
            _combinedUrlBuilder = combinedUrlBuilder;
            _cacheService = cacheService;
            _proxyUrlBuilder = proxyUrlBuilder;
            _proxyPool = proxyPool;
        }

        public async Task<ResourceInformationDto> Handle(GetMediaQuery request, CancellationToken cancellationToken)
        {
            var platform = PlatformIdentifierService.IdentifyPlatform(request.Query);

            if (platform == Platform.Spotify)
                return await HandleSpotify(request.Query, cancellationToken);

            else if (platform == Platform.TikTok)
                return await HandleTikTok(request.Query, cancellationToken);

            var resultData = await YT_DLP_Fetch(request.Query);

            if (resultData.ResultType == MetadataType.Video)
            {
                string thumbnailUrl = resultData.Thumbnail;

                // Try to find largest square thumbnail if available
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

                // First, check if we have formats that already contain both video AND audio
                var combinedFormats = ExtractCombinedFormatsAndCacheNewUrl(resultData.Formats);

                StreamReferenceDto? combinedReferences = null;
                StreamReferenceDto? audioReferences = null;

                // Offered alongside the three fixed rungs: a source usually has several more
                // steps than that, and which ones are worth showing is the client's call.
                var videoRenditions = new List<MediaRenditionDto>();
                var audioRenditions = _proxyUrlBuilder.BuildRenditions(
                    RenditionPicker.PickAudio(AudioSources(resultData.Formats)),
                    _cacheService,
                    isAudio: true,
                    proxy: _fetchProxy);

                if (HasValidUrls(combinedFormats))
                {
                    // We have pre-muxed formats! Use them directly (FAST!)
                    // These go through the proxy endpoint, not the combine endpoint
                    combinedReferences = _proxyUrlBuilder.BuildProxyReferences(combinedFormats, _cacheService, proxy: _fetchProxy);

                    // Still extract audio-only for "Audio Only" option
                    var audioUrls = ExtractThreeAudioQualitiesAndCacheNewUrl(resultData.Formats);
                    audioReferences = _proxyUrlBuilder.BuildProxyReferences(audioUrls, _cacheService, forceAudio: true, proxy: _fetchProxy);

                    // Pre-muxed formats are served as they are, so they are renditions of the
                    // pass-through kind rather than something to combine.
                    videoRenditions = _proxyUrlBuilder.BuildRenditions(
                        RenditionPicker.PickVideo(PreMuxedSources(resultData.Formats), perContainer: true),
                        _cacheService,
                        isAudio: false,
                        proxy: _fetchProxy);
                }
                else
                {
                    // No combined formats available, use separate streams (slower, needs FFmpeg)
                    var videoUrls = ExtractThreeVideoQualitiesAndCacheNewUrl(resultData.Formats);
                    var audioUrls = ExtractThreeAudioQualitiesAndCacheNewUrl(resultData.Formats);

                    // Build combined references using the combine endpoint (FFmpeg muxing)
                    combinedReferences = _combinedUrlBuilder.BuildCombinedReferences(videoUrls, audioUrls, _cacheService, _fetchProxy);

                    // Build proxy references for audio-only option
                    audioReferences = _proxyUrlBuilder.BuildProxyReferences(audioUrls, _cacheService, forceAudio: true, proxy: _fetchProxy);

                    // Each video step is paired with the best audio for muxing.
                    videoRenditions = _combinedUrlBuilder.BuildCombinedRenditions(
                        RenditionPicker.PickVideo(VideoOnlySources(resultData.Formats)),
                        RenditionPicker.PickAudio(AudioSources(resultData.Formats), count: 1).FirstOrDefault(),
                        _cacheService,
                        _fetchProxy);
                }

                if (combinedReferences != null)
                {
                    combinedReferences.Renditions = videoRenditions;
                }

                if (audioReferences != null)
                {
                    audioReferences.Renditions = audioRenditions;
                }

                return new ResourceInformationDto
                {
                    Type = MediaType.Media,
                    MediaItems =
                    [
                            new MediaInformationDto
                            {
                                RequestedUrl = request.Query,
                                Video = combinedReferences,  // Either pre-muxed or FFmpeg-combined
                                Audio = audioReferences,      // Audio-only option
                                CoverUrl = thumbnailUrl,
                                Title = resultData.Title,
                                Author = resultData.Uploader
                            }
                    ]
                };
            }
            else
                throw new NotSupportedException("This isn't implemented yet");
        }

        private StreamingUrlDto ExtractCombinedFormatsAndCacheNewUrl(FormatData[] formatData)
        {
            // Get formats that already have both video AND audio (no muxing needed!)
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

        private StreamingUrlDto ExtractThreeVideoQualitiesAndCacheNewUrl(FormatData[] formatData)
        {
            // Only get video-only formats (for separate stream approach)
            // These will be combined with audio using FFmpeg
            var videoOnlyFormats = formatData
                .Where(f =>
                    !string.IsNullOrEmpty(f.VideoCodec) &&
                    f.VideoCodec != "none" &&
                    (string.IsNullOrEmpty(f.AudioCodec) || f.AudioCodec == "none") && // Video only!
                    !f.Protocol.Contains("mhtml") &&
                    !f.Protocol.Contains("m3u8")
                )
                .OrderByDescending(f => f.Height ?? 0)
                .ThenByDescending(f => f.Bitrate)
                .ToList();

            // Prefer MP4 if available
            var mp4Formats = videoOnlyFormats
                .Where(f => f.Extension?.Equals(".mp4", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            if (mp4Formats.Any())
            {
                videoOnlyFormats = mp4Formats;
            }

            var bestVideo = videoOnlyFormats.FirstOrDefault();
            var mediumVideo = videoOnlyFormats.ElementAtOrDefault(videoOnlyFormats.Count() / 2);
            var worstVideo = videoOnlyFormats.LastOrDefault();

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

        /// <summary>
        /// Formats that already carry both tracks, so they can be served untouched.
        /// </summary>
        private static IEnumerable<RenditionSource> PreMuxedSources(FormatData[] formatData) =>
            formatData
                .Where(f =>
                    !string.IsNullOrEmpty(f.Url) &&
                    !string.IsNullOrEmpty(f.VideoCodec) && f.VideoCodec != "none" &&
                    !string.IsNullOrEmpty(f.AudioCodec) && f.AudioCodec != "none" &&
                    !f.Protocol.Contains("mhtml") &&
                    !f.Protocol.Contains("m3u8"))
                .Select(ToSource);

        /// <summary>Video tracks without audio, which have to be muxed before use.</summary>
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
                // Weighted the same way the three fixed rungs are, so the top rendition and
                // "best" do not disagree about which format wins.
                .OrderByDescending(f => f.Bitrate * OpusQualityFactor(f.AudioCodec))
                .Select(ToSource);

        private static RenditionSource ToSource(FormatData format) => new(
            format.Url,
            format.Format,
            format.Extension,
            format.Height,
            format.Bitrate,
            (long?)(format.FileSize ?? format.ApproximateFileSize));

        /// <summary>
        /// Opus is worth roughly 20% more than AAC at the same bitrate, so it is weighted that
        /// way rather than compared on the raw number. YouTube offers the two within a kbps of
        /// each other, which would otherwise make the pick a coin toss.
        /// </summary>
        private static double OpusQualityFactor(string? audioCodec) =>
            audioCodec?.StartsWith("opus", StringComparison.OrdinalIgnoreCase) == true ? 1.2 : 1.0;

        private StreamingUrlDto ExtractThreeAudioQualitiesAndCacheNewUrl(FormatData[] formatData)
        {
            // Ranked purely by bitrate. MP3 and M4A used to be preferred because everything
            // else was re-encoded to MP3 anyway, so the cheaper source won; sources are now
            // passed through untouched, which makes YouTube's Opus the better pick over the
            // AAC it also offers at a lower bitrate.
            var audioFormats = formatData
                .Where(f =>
                    !string.IsNullOrEmpty(f.AudioCodec) &&
                    f.Format.Contains("audio") &&
                    !f.Protocol.Contains("mhtml") &&
                    !f.Protocol.Contains("m3u8") &&
                    f.AudioBitrate != null &&
                    f.AudioBitrate != 0
                )
                .OrderByDescending(f => f.Bitrate * OpusQualityFactor(f.AudioCodec))
                .ToList();

            var bestAudio = audioFormats.FirstOrDefault();
            var mediumAudio = audioFormats.ElementAtOrDefault(audioFormats.Count() / 2);
            var worstAudio = audioFormats.LastOrDefault();

            return new StreamingUrlDto
            {
                BestQualityDescription = bestAudio?.Format,
                BestQuality = bestAudio?.Url,
                BestQualityFileExtension = bestAudio?.Extension,

                MediumQualityDescription = mediumAudio?.Format,
                MediumQuality = mediumAudio?.Url,
                MediumQualityFileExtension = mediumAudio?.Extension,

                WorstQualityDescription = worstAudio?.Format,
                WorstQuality = worstAudio?.Url,
                WorstQualityFileExtension = worstAudio?.Extension,
            };
        }



        private async Task<VideoData> YT_DLP_Fetch(string query, OptionSet? options = null)
        {
            options ??= new OptionSet { DumpSingleJson = true };

            if (!Uri.IsWellFormedUriString(query, UriKind.Absolute))
            {
                _fetchProxy = _proxyPool.Next();

                var searchOptions = new OptionSet
                {
                    NoPlaylist = true,
                    Proxy = _fetchProxy,
                };

                var searchResult = await _youtubeDL.RunVideoDataFetch($"ytsearch:{query}", overrideOptions: searchOptions);
                query = searchResult.Data.Entries.First().Url;
            }

            // A failed fetch is retried through the next proxy, since the usual cause is the
            // current one being blocked. Capped at 3 so a dead list still fails quickly.
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

                // Separated from a plain failure because the two mean different things to
                // whoever pasted the link: DRM is a refusal, not a bad address.
                if (YtDlpErrors.IsDrmProtected(result.ErrorOutput))
                    throw new NotSupportedException("This media is DRM protected and cannot be downloaded.");

                throw new ArgumentException($"Failed to fetch data: {errors}");
            }

            return result.Data;
        }

        private async Task<ResourceInformationDto> HandleSpotify(string query, CancellationToken cancellationToken)
        {
            var track = await _spotifyMetadataService.GetTrackAsync(query, cancellationToken);

            // Spotify only supplies the metadata; the audio comes from the matching
            // YouTube Music result.
            var searchQuery = YouTubeMusicMatcher.SearchQuery(track.Artist, track.Title);

            var response = await _ytmSearchClient.SearchTracksAsync(new YTMusicAPI.Model.QueryRequest
            {
                Query = searchQuery
            }, cancellationToken);

            var results = response.Result.ToList();

            // Taking the first hit matches covers, karaoke versions and instrumentals as readily as
            // the real recording, so score the candidates instead.
            var candidates = results
                .Select(r => new MatchCandidate(
                    r.Title ?? string.Empty,
                    r.Author,
                    (long)(r.Duration?.TotalSeconds ?? 0),
                    r.Author ?? string.Empty))
                .ToList();

            var best = YouTubeMusicMatcher.BestMatch(
                candidates,
                track.Title,
                track.Artist,
                track.DurationMs,
                officialShelf: true);

            if (best is null)
                throw new ArgumentException($"No YouTube Music match found for '{searchQuery}'");

            // Reference equality, not IndexOf: MatchCandidate is a record, so two results with the
            // same title, artist and length would compare equal and resolve to the wrong URL.
            var ytmTrackUrl = results[candidates.FindIndex(c => ReferenceEquals(c, best))].Url;

            var result = await YT_DLP_Fetch(ytmTrackUrl);

            var audioUrls = ExtractThreeAudioQualitiesAndCacheNewUrl(result.Formats);

            // Build proxy references
            // Force audio mode for Spotify tracks
            var audioReferences = _proxyUrlBuilder.BuildProxyReferences(audioUrls, _cacheService, forceAudio: true, proxy: _fetchProxy);

            // Spotify typically only has audio, so combined URLs would be null

            return new ResourceInformationDto
            {
                Type = MediaType.Media,
                MediaItems =
                [
                    new MediaInformationDto
                    {
                        RequestedUrl = query,
                        Video = null,  // Spotify has no video
                        Audio = audioReferences,  // Audio-only
                        CoverUrl = track.CoverUrl,
                        Title = track.Title,
                        Author = track.Artist,
                    }
                ]
            };
        }

        private async Task<ResourceInformationDto> HandleTikTok(string query, CancellationToken cancellationToken)
        {
            var mediaInformation = await _tikTokDllFetcherService.FetchLinkAsync(query, cancellationToken: cancellationToken);

            return new ResourceInformationDto
            {
                Type = MediaType.Media,
                MediaItems = [mediaInformation]
            };
        }
    }
}
