using ArgonFetch.Application.Dtos;
using MediatR;
using SpotifyAPI.Web;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace ArgonFetch.Application.Queries
{
    public class GetPlaylistQuery : IRequest<PlaylistInformationDto>
    {
        public GetPlaylistQuery(string url)
        {
            Url = url;
        }

        public string Url { get; set; }
    }

    public class GetPlaylistQueryHandler : IRequestHandler<GetPlaylistQuery, PlaylistInformationDto>
    {
        private readonly SpotifyClient _spotifyClient;
        private readonly YoutubeDL _youtubeDL;

        public GetPlaylistQueryHandler(SpotifyClient spotifyClient, YoutubeDL youtubeDL)
        {
            _spotifyClient = spotifyClient;
            _youtubeDL = youtubeDL;
            _youtubeDL.YoutubeDLPath = "yt-dlp";
            _youtubeDL.FFmpegPath = "ffmpeg";
        }

        public async Task<PlaylistInformationDto> Handle(GetPlaylistQuery request, CancellationToken cancellationToken)
        {
            try
            {
                var options = new OptionSet
                {
                    DumpSingleJson = true,
                };

                var result = await _youtubeDL.RunVideoDataFetch(request.Url, overrideOptions: options);
                if (!result.Success)
                {
                    throw new Exception($"Failed to fetch playlist data: {string.Join(", ", result.ErrorOutput)}");
                }

                var playlistData = result.Data;
                var mediaItems = playlistData.Entries?.Select(entry => new MediaInformationDto
                {
                    RequestedUrl = entry.Url ?? entry.WebpageUrl ?? string.Empty,
                    StreamingUrl = entry.Url ?? string.Empty,
                    CoverUrl = GetBestThumbnail(entry.Thumbnails) ?? entry.Thumbnail ?? string.Empty,
                    Title = entry.Title ?? string.Empty,
                    Author = entry.Uploader ?? string.Empty
                }).ToList() ?? new List<MediaInformationDto>();

                return new PlaylistInformationDto
                {
                    Title = playlistData.Title ?? string.Empty,
                    Author = playlistData.Uploader ?? string.Empty,
                    ImageUrl = GetBestThumbnail(playlistData.Thumbnails) ?? playlistData.Thumbnail ?? string.Empty,
                    RequestedUrl = request.Url,
                    MediaItems = mediaItems
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to fetch playlist information: {ex.Message}");
            }
        }

        private string GetBestThumbnail(IEnumerable<ThumbnailData> thumbnails)
        {
            if (thumbnails == null || !thumbnails.Any()) return null;

            return thumbnails
                .OrderByDescending(t => t.Width)
                .FirstOrDefault()
                ?.Url;
        }
    }
}
