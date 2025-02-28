using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Models;
using System.Text.Json;

namespace ArgonFetch.Application.Services.DDLFetcherServices
{
    public class DllFetcherService : IDllFetcher
    {
        private readonly YTDLPClient _ytdlpClient;

        public DllFetcherService(YTDLPClient ytdlpClient)
        {
            _ytdlpClient = ytdlpClient;
        }

        public async Task<MediaInformationDto> FetchLinkAsync(string query, DllFetcherOptions dllFetcherOptions = null, CancellationToken cancellationToken = default)
        {
            dllFetcherOptions ??= new DllFetcherOptions { MediaFormat = MediaFormat.Best };

            if (!Uri.IsWellFormedUriString(query, UriKind.Absolute))
            {
                var searchQuery = $"ytsearch:{query}";
                var searchJson = await _ytdlpClient.GetVideoInfoAsync(searchQuery);
                var searchData = JsonSerializer.Deserialize<VideoInfo>(searchJson);
                query = searchData.Url;
            }

            var videoJson = await _ytdlpClient.GetVideoInfoAsync(query);
            var videoData = JsonSerializer.Deserialize<VideoInfo>(videoJson);

            if (videoData == null)
            {
                throw new Exception("Failed to fetch video data");
            }

            string thumbnailUrl = videoData.Thumbnail;

            // Try to find largest square thumbnail if available
            if (videoData.Thumbnails?.Any() == true)
            {
                var squareThumbnails = videoData.Thumbnails
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

            return new MediaInformationDto
            {
                RequestedUrl = query,
                StreamingUrl = videoData.Url,
                CoverUrl = thumbnailUrl,
                Title = videoData.Title,
                Author = videoData.Uploader
            };
        }

        private string GetFormatString(MediaFormat format) => format switch
        {
            MediaFormat.Best => "best",
            MediaFormat.Worst => "worst",
            MediaFormat.BestAudio => "bestaudio",
            MediaFormat.WorstAudio => "worstaudio",
            _ => throw new ArgumentException($"Unsupported format: {format}")
        };
    }
}
