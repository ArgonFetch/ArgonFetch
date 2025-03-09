using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Services;
using MediatR;
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

        public GetMediaQueryHandler(
            SpotifyClient spotifyClient,
            YTMusicAPI.SearchClient ytmSearchClient,
            YoutubeDL youtubeDL
            )
        {
            _spotifyClient = spotifyClient;
            _ytmSearchClient = ytmSearchClient;
            _youtubeDL = youtubeDL;
        }

        public async Task<ResourceInformationDto> Handle(GetMediaQuery request, CancellationToken cancellationToken)
        {
            var platform = PlatformIdentifierService.IdentifyPlatform(request.Query);

            if (platform == Platform.Spotify)
            {
                return await HandleSpotify(request.Query, cancellationToken);
            }

            var resultData = await Search(request.Query);

            if (resultData.ResultType == MetadataType.Playlist)
            {
                throw new NotSupportedException("Playlists are not supported yet.");
                try
                {
                    var mediaItems = resultData.Entries?.Select(entry => new MediaInformationDto
                    {
                        RequestedUrl = entry.Url ?? entry.WebpageUrl ?? string.Empty,
                        StreamingUrl = entry.Url ?? string.Empty,
                        CoverUrl = GetBestThumbnail(entry.Thumbnails) ?? entry.Thumbnail ?? string.Empty,
                        Title = entry.Title ?? string.Empty,
                        Author = entry.Uploader ?? string.Empty
                    }).ToList() ?? new List<MediaInformationDto>();

                    var returnDto = new ResourceInformationDto
                    {
                        Type = MediaType.PlayList,
                        Title = resultData.Title ?? string.Empty,
                        Author = resultData.Uploader ?? string.Empty,
                        CoverUrl = GetBestThumbnail(resultData.Thumbnails) ?? resultData.Thumbnail ?? string.Empty,
                        MediaItems = mediaItems
                    };
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to fetch playlist information: {ex.Message}");
                }
            }
            else
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

                var streamingUrl = resultData.Url ?? await GetBestStreamingUrl(resultData.Formats);

                return new ResourceInformationDto
                {
                    Type = MediaType.Media,
                    MediaItems =
                    [
                            new MediaInformationDto
                            {
                                RequestedUrl = request.Query,
                                StreamingUrl = streamingUrl,
                                CoverUrl = thumbnailUrl,
                                Title = resultData.Title,
                                Author = resultData.Uploader
                            }
                    ]
                };
            }
        }

        private async Task<VideoData> Search(string query, OptionSet? options = null)
        {
            options ??= new OptionSet { DumpSingleJson = true };

            if (!Uri.IsWellFormedUriString(query, UriKind.Absolute))
            {
                var searchOptions = new OptionSet
                {
                    Format = "best",
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

        private async Task<string> GetBestStreamingUrl(FormatData[] formatData)
        {
            if (formatData == null || !formatData.Any())
                return string.Empty;

            var bestFormat = formatData
                .Where(f => !string.IsNullOrEmpty(f.AudioCodec) && f.AudioCodec != "none")
                .OrderByDescending(f => f.AudioBitrate)
                .ThenByDescending(f => f.AudioSamplingRate)
                .FirstOrDefault();

            return await Task.FromResult(bestFormat?.Url ?? string.Empty);
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

            var downloadOptions = new OptionSet
            {
                Format = "best",
                NoPlaylist = true,
            };

            var result = await Search(ytmTrackUrl, downloadOptions);

            return new ResourceInformationDto
            {
                Type = MediaType.Media,
                MediaItems = new MediaInformationDto[]
                {
                    new MediaInformationDto
                    {
                        RequestedUrl = query,
                        StreamingUrl = result.Url,
                        CoverUrl = searchResponse.Album.Images.First().Url,
                        Title = searchResponse.Name,
                        Author = searchResponse.Artists.First().Name
                    }
                }
            };
        }

        private string GetBestThumbnail(IEnumerable<ThumbnailData> thumbnails)
        {
            if (thumbnails == null || !thumbnails.Any()) return null;

#pragma warning disable CS8603 // Possible null reference return.
            return thumbnails
                .OrderByDescending(t => t.Width)
                .FirstOrDefault()
                ?.Url;
#pragma warning restore CS8603 // Possible null reference return.
        }
    }
}
