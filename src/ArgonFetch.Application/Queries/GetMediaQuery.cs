using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Services;
using ArgonFetch.Application.Services.DDLFetcherServices;
using MediatR;
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

        public GetMediaQueryHandler(
            SpotifyClient spotifyClient,
            YTMusicAPI.SearchClient ytmSearchClient,
            YoutubeDL youtubeDL,
            TikTokDllFetcherService tikTokDllFetcherService,
            IMemoryCache memoryCache
            )
        {
            _spotifyClient = spotifyClient;
            _ytmSearchClient = ytmSearchClient;
            _youtubeDL = youtubeDL;
            _tikTokDllFetcherService = tikTokDllFetcherService;
            _memoryCache = memoryCache;
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

                return new ResourceInformationDto
                {
                    Type = MediaType.Media,
                    MediaItems =
                    [
                            new MediaInformationDto
                            {
                                RequestedUrl = request.Query,
                                StreamingVideoUrls = ExtractThreeVideoQualitiesAndCacheNewUrl(resultData.Formats),
                                StreamingAudioUrls = ExtractThreeAudioQualitiesAndCacheNewUrl(resultData.Formats),
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

        private StreamingUrlDto ExtractThreeVideoQualitiesAndCacheNewUrl(FormatData[] formatData)
        {
            var mp4Formats = formatData
                .Where(f =>
                    !f.Format.Contains("audio") &&
                    !f.Protocol.Contains("mhtml") &&
                    !f.Protocol.Contains("m3u8")
                )
                .OrderByDescending(f => f.Bitrate);

            var bestVideo = mp4Formats.FirstOrDefault();
            var mediumVideo = mp4Formats.ElementAtOrDefault(mp4Formats.Count() / 2);
            var worstVideo = mp4Formats.LastOrDefault();

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
            var mp3Formats = formatData
                .Where(f =>
                    !string.IsNullOrEmpty(f.AudioCodec) &&
                    f.Format.Contains("audio") &&
                    !f.Protocol.Contains("mhtml") &&
                    !f.Protocol.Contains("m3u8") &&
                    f.AudioBitrate != null &&
                    f.AudioBitrate != 0
                )
                .OrderByDescending(f => f.Bitrate);

            var bestAudio = mp3Formats.FirstOrDefault();
            var mediumAudio = mp3Formats.ElementAtOrDefault(mp3Formats.Count() / 2);
            var worstAudio = mp3Formats.LastOrDefault();

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

            return new ResourceInformationDto
            {
                Type = MediaType.Media,
                MediaItems =
                [
                    new MediaInformationDto
                    {
                        RequestedUrl = query,
                        StreamingAudioUrls = ExtractThreeAudioQualitiesAndCacheNewUrl(result.Formats),
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
