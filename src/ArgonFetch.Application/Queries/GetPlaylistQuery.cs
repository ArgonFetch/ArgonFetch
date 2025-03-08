using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Services;
using MediatR;
using SpotifyAPI.Web;
using System.Text.Json;

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
        private readonly YTDLPClient _ytdlpClient;

        public GetPlaylistQueryHandler(SpotifyClient spotifyClient, YTDLPClient yTDLPClient)
        {
            _spotifyClient = spotifyClient;
            _ytdlpClient = yTDLPClient;
        }

        public async Task<PlaylistInformationDto> Handle(GetPlaylistQuery request, CancellationToken cancellationToken)
        {
            var url = request.Url;
            var platform = PlatformIdentifierService.IdentifyPlatform(url);
            var contentType = await MediaContentIdentifierService.IdentifyContent(url, platform);

            if (new[] { ContentType.Media, ContentType.Url, ContentType.SearchTerm }.Contains(contentType))
                throw new NotSupportedException($"{contentType} is not a Playlist");

            return platform == Platform.Spotify
                ? await HandleSpotifyUrl(url)
                : await HandleYouTubeOrSoundCloudUrl(url);
        }

        private async Task<PlaylistInformationDto> HandleSpotifyUrl(string url)
        {
            var contentType = await MediaContentIdentifierService.IdentifyContent(url);

            var uri = new Uri(url);
            var segments = uri.Segments;
            var id = segments[^1].Trim('/');

            if (contentType == ContentType.Playlist)
            {
                var playlist = await _spotifyClient.Playlists.Get(id);
                if (playlist == null) throw new ArgumentException("Playlist not found");

                return new PlaylistInformationDto
                {
                    Title = playlist.Name ?? "Unknown Playlist",
                    Author = playlist.Owner?.DisplayName ?? "Unknown Author",
                    ImageUrl = playlist.Images?.FirstOrDefault()?.Url ?? string.Empty,
                    RequestedUrl = url,
                    MediaItems = playlist.Tracks?.Items?
                        .Select(item => item.Track as FullTrack)
                        .Where(track => track != null)
                        .Select(track => new MediaInformationDto
                        {
                            RequestedUrl = $"https://open.spotify.com/track/{(track?.Uri != null ? track.Uri.Split(":").Last() : string.Empty)}",
                            StreamingUrl = string.Empty,
                            CoverUrl = track?.Album?.Images?.FirstOrDefault()?.Url ?? string.Empty,
                            Title = track?.Name ?? "Unknown Track",
                            Author = string.Join(", ", track?.Artists?.Select(a => a.Name) ?? Array.Empty<string>())
                        }).ToList() ?? new List<MediaInformationDto>()
                };
            }
            else if (contentType == ContentType.SpotifyAlbum)
            {
                var album = await _spotifyClient.Albums.Get(id);
                if (album == null) throw new ArgumentException("Album not found");

                var tracks = await _spotifyClient.Albums.GetTracks(id);
                if (tracks == null) throw new ArgumentException("Album tracks not found");

                return new PlaylistInformationDto
                {
                    Title = album.Name ?? "Unknown Album",
                    Author = string.Join(", ", album.Artists?.Select(a => a.Name) ?? Array.Empty<string>()),
                    ImageUrl = album.Images?.FirstOrDefault()?.Url ?? string.Empty,
                    RequestedUrl = url,
                    MediaItems = tracks.Items?.Select(track => new MediaInformationDto
                    {
                        RequestedUrl = track.Uri ?? string.Empty,
                        StreamingUrl = track.Uri ?? string.Empty,
                        CoverUrl = album.Images?.FirstOrDefault()?.Url ?? string.Empty,
                        Title = track.Name ?? "Unknown Track",
                        Author = string.Join(", ", track.Artists?.Select(a => a.Name) ?? Array.Empty<string>())
                    }).ToList() ?? new List<MediaInformationDto>()
                };
            }

            throw new ArgumentException("Unsupported Spotify content type");
        }

        private async Task<PlaylistInformationDto> HandleYouTubeOrSoundCloudUrl(string url)
        {
            try
            {
                var optionalParams = new[] { "--flat-playlist", "--dump-single-json" };
                var videoInfo = await _ytdlpClient.GetVideoInfoAsync(url, optionalParams);

                var mediaItems = new List<MediaInformationDto>();
                var playlistInfo = JsonDocument.Parse(videoInfo).RootElement;

                if (playlistInfo.TryGetProperty("entries", out var entries))
                {
                    mediaItems = entries.EnumerateArray()
                        .Select(entry => new MediaInformationDto
                        {
                            RequestedUrl = GetJsonValue(entry, "url") ?? GetJsonValue(entry, "webpage_url") ?? string.Empty,
                            StreamingUrl = string.Empty,
                            CoverUrl = GetThumbnailUrl(entry),
                            Title = GetJsonValue(entry, "title") ?? "",
                            Author = GetJsonValue(entry, "uploader") ?? ""
                        })
                        .ToList();
                }

                return new PlaylistInformationDto
                {
                    Title = GetJsonValue(playlistInfo, "title") ?? "",
                    Author = GetJsonValue(playlistInfo, "uploader") ?? "",
                    ImageUrl = GetThumbnailUrl(playlistInfo),
                    RequestedUrl = url,
                    MediaItems = mediaItems
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to fetch playlist information: {ex.Message}");
            }
        }

        private static string GetJsonValue(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) ? property.GetString() : null;
        }

        private static string GetThumbnailUrl(JsonElement element)
        {
            if (element.TryGetProperty("thumbnails", out var thumbnails) && thumbnails.ValueKind == JsonValueKind.Array)
            {
                // Select the thumbnail with the highest resolution
                var bestThumbnail = thumbnails.EnumerateArray()
                    .OrderByDescending(thumb =>
                        thumb.TryGetProperty("width", out var widthProp) ? widthProp.GetInt32() : 0)
                    .FirstOrDefault();

                if (bestThumbnail.TryGetProperty("url", out var url))
                {
                    return url.GetString() ?? string.Empty;
                }
            }

            // Fallback to direct thumbnail property if it exists
            if (element.TryGetProperty("thumbnail", out var thumbnail) && thumbnail.ValueKind == JsonValueKind.String)
            {
                return thumbnail.GetString() ?? string.Empty;
            }

            return string.Empty;
        }
    }
}
