using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Services;
using ArgonFetch.Application.Services.DDLFetcherServices;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using SpotifyAPI.Web;
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
        private readonly SpotifyClient _spotifyClient;
        private readonly YTMusicAPI.SearchClient _ytmSearchClient;
        private readonly TikTokDllFetcherService _tikTokDllFetcherService;
        private readonly IMemoryCache _memoryCache;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ICombinedStreamUrlBuilder _combinedUrlBuilder;
        private readonly IMediaUrlCacheService _cacheService;
        private readonly IProxyUrlBuilder _proxyUrlBuilder;

        public GetMediaQueryHandler(
            SpotifyClient spotifyClient,
            YTMusicAPI.SearchClient ytmSearchClient,
            YoutubeDL youtubeDL,
            TikTokDllFetcherService tikTokDllFetcherService,
            IMemoryCache memoryCache,
            IHttpContextAccessor httpContextAccessor,
            ICombinedStreamUrlBuilder combinedUrlBuilder,
            IMediaUrlCacheService cacheService,
            IProxyUrlBuilder proxyUrlBuilder
            )
        {
            _spotifyClient = spotifyClient;
            _ytmSearchClient = ytmSearchClient;
            _youtubeDL = youtubeDL;
            _tikTokDllFetcherService = tikTokDllFetcherService;
            _memoryCache = memoryCache;
            _httpContextAccessor = httpContextAccessor;
            _combinedUrlBuilder = combinedUrlBuilder;
            _cacheService = cacheService;
            _proxyUrlBuilder = proxyUrlBuilder;
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

                StreamingUrlDto? combinedUrls = null;
                StreamingUrlDto? videoUrls = null;
                StreamingUrlDto? audioUrls = null;

                if (HasValidUrls(combinedFormats))
                {
                    // We have pre-muxed formats! Use them directly (FAST!)
                    // These go through the proxy endpoint, not the combine endpoint
                    if (_httpContextAccessor.HttpContext != null)
                    {
                        combinedUrls = _proxyUrlBuilder.BuildProxyUrls(combinedFormats, _httpContextAccessor.HttpContext.Request, _cacheService);
                    }
                    else
                    {
                        combinedUrls = combinedFormats;
                    }

                    // Still extract audio-only for "Audio Only" option
                    audioUrls = ExtractThreeAudioQualitiesAndCacheNewUrl(resultData.Formats);
                    if (_httpContextAccessor.HttpContext != null)
                    {
                        audioUrls = _proxyUrlBuilder.BuildProxyUrls(audioUrls, _httpContextAccessor.HttpContext.Request, _cacheService, forceAudio: true);
                    }
                }
                else
                {
                    // No combined formats available, use separate streams (slower, needs FFmpeg)
                    videoUrls = ExtractThreeVideoQualitiesAndCacheNewUrl(resultData.Formats);
                    audioUrls = ExtractThreeAudioQualitiesAndCacheNewUrl(resultData.Formats);

                    // Build combined URLs using the combine endpoint (FFmpeg muxing)
                    combinedUrls = _httpContextAccessor.HttpContext != null
                        ? _combinedUrlBuilder.BuildCombinedUrls(videoUrls, audioUrls, _httpContextAccessor.HttpContext.Request, _cacheService)
                        : null;

                    // Build proxy URLs for separate streams if needed
                    if (_httpContextAccessor.HttpContext != null)
                    {
                        videoUrls = _proxyUrlBuilder.BuildProxyUrls(videoUrls, _httpContextAccessor.HttpContext.Request, _cacheService);
                        audioUrls = _proxyUrlBuilder.BuildProxyUrls(audioUrls, _httpContextAccessor.HttpContext.Request, _cacheService, forceAudio: true);
                    }
                }

                return new ResourceInformationDto
                {
                    Type = MediaType.Media,
                    MediaItems =
                    [
                            new MediaInformationDto
                            {
                                RequestedUrl = request.Query,
                                Video = combinedUrls,  // Either pre-muxed or FFmpeg-combined
                                Audio = audioUrls,      // Audio-only option
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

        private StreamingUrlDto ExtractThreeAudioQualitiesAndCacheNewUrl(FormatData[] formatData)
        {
            // First try to get MP3/M4A formats (no conversion needed, faster)
            var audioFormats = formatData
                .Where(f =>
                    !string.IsNullOrEmpty(f.AudioCodec) &&
                    f.Format.Contains("audio") &&
                    !f.Protocol.Contains("mhtml") &&
                    !f.Protocol.Contains("m3u8") &&
                    f.AudioBitrate != null &&
                    f.AudioBitrate != 0 &&
                    (f.Extension?.Equals(".mp3", StringComparison.OrdinalIgnoreCase) == true ||
                     f.Extension?.Equals(".m4a", StringComparison.OrdinalIgnoreCase) == true)
                )
                .OrderByDescending(f => f.Bitrate)
                .ToList();

            // If no MP3/M4A formats, fall back to any audio format
            if (!audioFormats.Any())
            {
                audioFormats = formatData
                    .Where(f =>
                        !string.IsNullOrEmpty(f.AudioCodec) &&
                        f.Format.Contains("audio") &&
                        !f.Protocol.Contains("mhtml") &&
                        !f.Protocol.Contains("m3u8") &&
                        f.AudioBitrate != null &&
                        f.AudioBitrate != 0
                    )
                    .OrderByDescending(f => f.Bitrate)
                    .ToList();
            }

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
                var searchOptions = new OptionSet
                {
                    NoPlaylist = true,
                };

                var searchResult = await _youtubeDL.RunVideoDataFetch($"ytsearch:{query}", overrideOptions: searchOptions);
                query = searchResult.Data.Entries.First().Url;
            }

            var result = await _youtubeDL.RunVideoDataFetch(query, overrideOptions: options);
            if (!result.Success)
                throw new ArgumentException($"Failed to fetch data: {string.Join(", ", result.ErrorOutput)}");

            return result.Data;
        }

        private async Task<ResourceInformationDto> HandleSpotify(string query, CancellationToken cancellationToken)
        {
            var uri = new Uri(query);
            var segments = uri.Segments;
            var searchResponse = await _spotifyClient.Tracks.Get(segments.Last(), cancellationToken);

            if (searchResponse == null)
                throw new ArgumentException("Track not found");

            var response = await _ytmSearchClient.SearchTracksAsync(new YTMusicAPI.Model.QueryRequest
            {
                Query = $"{searchResponse.Name} by {searchResponse.Artists.First().Name}"
            }, cancellationToken);

            var ytmTrackUrl = response.Result.First().Url;

            var result = await YT_DLP_Fetch(ytmTrackUrl);

            var audioUrls = ExtractThreeAudioQualitiesAndCacheNewUrl(result.Formats);

            // Build proxy URLs if HTTP context is available
            // Force audio mode for Spotify tracks
            if (_httpContextAccessor.HttpContext != null)
            {
                audioUrls = _proxyUrlBuilder.BuildProxyUrls(audioUrls, _httpContextAccessor.HttpContext.Request, _cacheService, forceAudio: true);
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
                        Audio = audioUrls,  // Audio-only
                        CoverUrl = searchResponse.Album.Images.First().Url,
                        Title = searchResponse.Name,
                        Author = searchResponse.Artists.First().Name,
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
