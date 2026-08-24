using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Services;
using ArgonFetch.Application.Services.DDLFetcherServices;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;
using YouTubeMusicAPI.Client;
using YouTubeMusicAPI.Models.Search;

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
        private readonly YouTubeMusicClient _ytmClient;
        private readonly TikTokDllFetcherService _tikTokDllFetcherService;
        private readonly IMemoryCache _memoryCache;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICombinedStreamUrlBuilder _combinedUrlBuilder;
        private readonly IMediaUrlCacheService _cacheService;
        private readonly IProxyUrlBuilder _proxyUrlBuilder;
        private readonly IProxyPool _proxyPool;
        private readonly IToolPaths _toolPaths;
        private readonly ILogger<GetMediaQueryHandler> _logger;

        // Enough to hold the album version when search leads with a radio edit and a remaster,
        // and few enough that a mistyped query does not drag twenty rows through matching.
        private const int SearchResultsToConsider = 20;

        // Wrong-recording candidates come in runs - a radio edit, a remaster and a twelve-inch
        // mix all sit above the album version - so checking a couple past the leader is worth a
        // fetch each. Beyond that the search itself was wrong.
        private const int MaxVerificationAttempts = 3;

        // A release and its Spotify entry differ by a second or two of trailing silence. A radio
        // edit differs by a minute, which is what this has to catch.
        private const double VerificationToleranceSec = 12.0;

        // The proxy the last extraction went through. Media URLs are signed for the IP that
        // requested them, so it has to travel with them to the stream endpoint.
        private string? _fetchProxy;

        public GetMediaQueryHandler(
            ISpotifyMetadataService spotifyMetadataService,
            YouTubeMusicClient ytmClient,
            YoutubeDL youtubeDL,
            TikTokDllFetcherService tikTokDllFetcherService,
            IMemoryCache memoryCache,
            IHttpContextAccessor httpContextAccessor,
            ICombinedStreamUrlBuilder combinedUrlBuilder,
            IMediaUrlCacheService cacheService,
            IProxyUrlBuilder proxyUrlBuilder,
            IProxyPool proxyPool,
            IToolPaths toolPaths,
            ILogger<GetMediaQueryHandler> logger
            )
        {
            _spotifyMetadataService = spotifyMetadataService;
            _ytmClient = ytmClient;
            _youtubeDL = youtubeDL;
            _tikTokDllFetcherService = tikTokDllFetcherService;
            _memoryCache = memoryCache;
            _httpContextAccessor = httpContextAccessor;
            _combinedUrlBuilder = combinedUrlBuilder;
            _cacheService = cacheService;
            _proxyUrlBuilder = proxyUrlBuilder;
            _proxyPool = proxyPool;
            _toolPaths = toolPaths;
            _logger = logger;
        }

        public async Task<ResourceInformationDto> Handle(GetMediaQuery request, CancellationToken cancellationToken)
        {
            var platform = PlatformIdentifierService.IdentifyPlatform(request.Query);

            if (platform == Platform.Spotify)
            {
                var contentType = await MediaContentIdentifierService.IdentifyContent(request.Query, platform);

                return contentType is ContentType.Playlist or ContentType.SpotifyAlbum
                    ? await HandleSpotifyCollection(request.Query, cancellationToken)
                    : await HandleSpotify(request.Query, cancellationToken);
            }

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

                // Carried into the cache so the stream endpoint can name and tag the file it
                // serves; by then only a key is left to identify the media by.
                var tags = new MediaTags(resultData.Title, resultData.Uploader);

                // Offered alongside the three fixed rungs: a source usually has several more
                // steps than that, and which ones are worth showing is the client's call.
                var videoRenditions = new List<MediaRenditionDto>();
                var audioRenditions = _proxyUrlBuilder.BuildRenditions(
                    RenditionPicker.PickAudio(AudioSources(resultData.Formats)),
                    _cacheService,
                    isAudio: true,
                    proxy: _fetchProxy,
                    tags: tags);

                if (HasValidUrls(combinedFormats))
                {
                    // We have pre-muxed formats! Use them directly (FAST!)
                    // These go through the proxy endpoint, not the combine endpoint
                    combinedReferences = _proxyUrlBuilder.BuildProxyReferences(combinedFormats, _cacheService, proxy: _fetchProxy, tags: tags);

                    // Still extract audio-only for "Audio Only" option
                    var audioUrls = ExtractThreeAudioQualitiesAndCacheNewUrl(resultData.Formats);
                    audioReferences = _proxyUrlBuilder.BuildProxyReferences(audioUrls, _cacheService, forceAudio: true, proxy: _fetchProxy, tags: tags);

                    // Pre-muxed formats are served as they are, so they are renditions of the
                    // pass-through kind rather than something to combine.
                    videoRenditions = _proxyUrlBuilder.BuildRenditions(
                        RenditionPicker.PickVideo(PreMuxedSources(resultData.Formats), perContainer: true),
                        _cacheService,
                        isAudio: false,
                        proxy: _fetchProxy,
                        tags: tags);
                }
                else
                {
                    // No combined formats available, use separate streams (slower, needs FFmpeg)
                    var videoUrls = ExtractThreeVideoQualitiesAndCacheNewUrl(resultData.Formats);
                    var audioUrls = ExtractThreeAudioQualitiesAndCacheNewUrl(resultData.Formats);

                    // Build combined references using the combine endpoint (FFmpeg muxing)
                    combinedReferences = _combinedUrlBuilder.BuildCombinedReferences(videoUrls, audioUrls, _cacheService, _fetchProxy, tags);

                    // Build proxy references for audio-only option
                    audioReferences = _proxyUrlBuilder.BuildProxyReferences(audioUrls, _cacheService, forceAudio: true, proxy: _fetchProxy, tags: tags);

                    // Each video step is paired with the best audio for muxing.
                    videoRenditions = _combinedUrlBuilder.BuildCombinedRenditions(
                        RenditionPicker.PickVideo(VideoOnlySources(resultData.Formats)),
                        RenditionPicker.PickAudio(AudioSources(resultData.Formats), count: 1).FirstOrDefault(),
                        _cacheService,
                        _fetchProxy,
                        tags);
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
        /// The watch page for a song id, which is what yt-dlp is given to fetch.
        /// </summary>
        private static string WatchUrl(string id) => $"https://music.youtube.com/watch?v={id}";

        /// <summary>
        /// Fetches candidates in order until one turns out to be the requested recording.
        /// </summary>
        /// <param name="requireDuration">
        /// Whether a candidate whose length cannot be confirmed may be accepted. It may on the
        /// normal path, where the title and credit already matched; it may not when the title
        /// was disregarded, because then the duration is the only thing identifying the track.
        /// </param>
        private async Task<VideoData?> FetchVerifiedAgainstSpotify(
            IReadOnlyList<MatchCandidate> ranked,
            List<MatchCandidate> candidates,
            List<SongSearchResult> results,
            SpotifyTrackMetadata track,
            CancellationToken cancellationToken,
            bool requireDuration = false)
        {
            VideoData? firstFetched = null;

            // Two beyond the leader. Each attempt is a yt-dlp call, and a search whose first
            // three results are all the wrong recording is not one more fetch away from success.
            foreach (var candidate in ranked.Take(MaxVerificationAttempts))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Reference equality, not IndexOf: MatchCandidate is a record, so two results with
                // the same title, artist and length would compare equal and resolve to the wrong URL.
                var url = WatchUrl(results[candidates.FindIndex(c => ReferenceEquals(c, candidate))].Id);

                VideoData fetched;

                try
                {
                    fetched = await YT_DLP_Fetch(url);
                }
                catch (ArgumentException)
                {
                    // A candidate that cannot be fetched is no worse than one that does not fit.
                    continue;
                }

                firstFetched ??= fetched;

                if (DurationFits(fetched, track.DurationMs))
                    return fetched;
            }

            // Nothing was confirmed. On the normal path the leader had already matched on title
            // and credit, so it is still the best answer available; on the credit-only path there
            // is nothing left to identify it by, and a wrong recording is worse than none.
            return requireDuration ? null : firstFetched;
        }

        /// <summary>
        /// Whether a fetched result runs to the length the request asked for. Unknown lengths on
        /// either side are not evidence against a candidate.
        /// </summary>
        private static bool DurationFits(VideoData fetched, long wantedMs)
        {
            if (wantedMs <= 0 || fetched.Duration is not > 0)
                return false;

            return Math.Abs(fetched.Duration.Value - wantedMs / 1000.0) <= VerificationToleranceSec;
        }

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

            // Not only for Instagram: an age-gated YouTube video needs a session too, and a
            // source that does not care simply ignores them.
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

                // Told apart from a missing page for the same reason as DRM: the link is right
                // and the fix is a cookies file, which the reader can actually act on.
                if (YtDlpErrors.NeedsSignedInSession(result.ErrorOutput))
                    throw new NotSupportedException(
                        _toolPaths.CookiesPath is null
                            ? "This source serves media only to a signed-in session. Set COOKIES_PATH to a Netscape-format cookies file exported from a logged-in browser."
                            : "This source rejected the configured session. The cookies file may have expired.");

                throw new ArgumentException($"Failed to fetch data: {errors}");
            }

            return result.Data;
        }

        /// <summary>
        /// An album or playlist, listed rather than resolved.
        /// <para>
        /// Each entry needs its own YouTube Music match and its own extraction, which is seconds
        /// of work apiece; doing that for a hundred of them before showing anything would take
        /// minutes and throw most of it away, since nobody downloads a whole playlist by
        /// accident. The listing carries what Spotify knows, and picking an entry fetches that
        /// one track through the path a single link already takes.
        /// </para>
        /// </summary>
        private async Task<ResourceInformationDto> HandleSpotifyCollection(string query, CancellationToken cancellationToken)
        {
            var collection = await _spotifyMetadataService.GetCollectionAsync(query, cancellationToken);

            if (collection.MayBeTruncated)
            {
                _logger.LogInformation(
                    "Spotify returned the maximum of {Count} entries for {Url}; there may be more it did not send.",
                    collection.Items.Count, query);
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
                        RequestedUrl = item.TrackUrl,
                        Title = item.Title,
                        Author = item.Artist,
                        // The listing has no per-track picture, so entries show the release's.
                        CoverUrl = collection.CoverUrl,
                        // Unresolved on purpose: see above.
                        Audio = null,
                        Video = null
                    })
                    .ToList()
            };
        }

        private async Task<ResourceInformationDto> HandleSpotify(string query, CancellationToken cancellationToken)
        {
            var track = await _spotifyMetadataService.GetTrackAsync(query, cancellationToken);

            // Spotify only supplies the metadata; the audio comes from the matching
            // YouTube Music result.
            var searchQuery = YouTubeMusicMatcher.SearchQuery(track.Artist, track.Title);

            var results = (await _ytmClient
                    .SearchAsync(searchQuery, SearchCategory.Songs)
                    .FetchItemsAsync(0, SearchResultsToConsider, cancellationToken))
                .OfType<SongSearchResult>()
                .ToList();

            // Taking the first hit matches covers, karaoke versions and instrumentals as readily as
            // the real recording, so score the candidates instead.
            var candidates = results
                .Select(r => new MatchCandidate(
                    r.Name,
                    string.Join(", ", r.Artists.Select(artist => artist.Name)),
                    (long)r.Duration.TotalSeconds,
                    // The release, which is where an instrumental or a solo cut declares itself.
                    // The previous client offered no album at all, so this carried the artist
                    // again and the check that reads it never had anything to find.
                    r.Album?.Name ?? string.Empty))
                .ToList();

            var ranked = YouTubeMusicMatcher.RankMatches(
                candidates,
                track.Title,
                track.Artist,
                track.DurationMs,
                officialShelf: true);

            // A safety net rather than the mechanism: search reports durations now, so ranking
            // usually settles which recording this is and the first candidate is the answer.
            // Fetching still confirms it, because a length search got wrong would otherwise be
            // discovered by whoever opened the file.
            var result = await FetchVerifiedAgainstSpotify(ranked, candidates, results, track, cancellationToken);

            if (result is null)
            {
                // Titles are often translated - Spotify says "REVENGE OF B" where YouTube Music
                // says the Japanese original - and then no candidate shares a single word with
                // what was asked for. The credit still matches, and the duration decides, so
                // this pass drops the title requirement and leans on the check instead.
                var byCredit = YouTubeMusicMatcher.RankByCreditOnly(candidates, track.Artist, track.DurationMs);

                result = await FetchVerifiedAgainstSpotify(byCredit, candidates, results, track, cancellationToken, requireDuration: true);
            }

            if (result is null)
                throw new ArgumentException($"No YouTube Music match found for '{searchQuery}'");

            var audioUrls = ExtractThreeAudioQualitiesAndCacheNewUrl(result.Formats);

            // Build proxy references
            // Force audio mode for Spotify tracks
            // Spotify's own metadata, not YouTube's: knowing the release better than YouTube
            // does is the reason the track was matched rather than simply searched for.
            var spotifyTags = new MediaTags(track.Title, track.Artist);

            var audioReferences = _proxyUrlBuilder.BuildProxyReferences(audioUrls, _cacheService, forceAudio: true, proxy: _fetchProxy, tags: spotifyTags);

            // The audio comes from YouTube Music, so it has the same renditions any other
            // YouTube track does. Without this a Spotify link fell back to "Best" and "Low",
            // which says nothing about what either one is.
            if (audioReferences != null)
            {
                audioReferences.Renditions = _proxyUrlBuilder.BuildRenditions(
                    RenditionPicker.PickAudio(AudioSources(result.Formats)),
                    _cacheService,
                    isAudio: true,
                    proxy: _fetchProxy,
                    tags: spotifyTags);
            }

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
