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
                var searchData = JsonDocument.Parse(searchJson).RootElement;
                query = searchData.GetProperty("url").GetString();
            }

            var options = new[] { "--flat-playlist" };
            var videoJson = await _ytdlpClient.GetVideoInfoAsync(query);
            var videoData = JsonDocument.Parse(videoJson).RootElement;

            if (videoData.ValueKind == JsonValueKind.Undefined)
            {
                throw new Exception("Failed to fetch video data");
            }

            string thumbnailUrl = videoData.TryGetProperty("thumbnail", out var thumbnail)
                ? thumbnail.GetString()
                : null;

            // Try to find largest square thumbnail if available
            if (videoData.TryGetProperty("thumbnails", out var thumbnailsElement) &&
                thumbnailsElement.ValueKind == JsonValueKind.Array)
            {
                var squareThumbnails = new List<(string url, int size)>();

                foreach (var thumbElement in thumbnailsElement.EnumerateArray())
                {
                    if (thumbElement.TryGetProperty("width", out var widthElement) &&
                        thumbElement.TryGetProperty("height", out var heightElement) &&
                        thumbElement.TryGetProperty("url", out var urlElement))
                    {
                        int width = widthElement.TryGetInt32(out var w) ? w : 0;
                        int height = heightElement.TryGetInt32(out var h) ? h : 0;

                        if (width > 0 && width == height)
                        {
                            squareThumbnails.Add((urlElement.GetString(), width));
                        }
                    }
                }

                if (squareThumbnails.Any())
                {
                    thumbnailUrl = squareThumbnails
                        .OrderByDescending(t => t.size)
                        .First()
                        .url;
                }
            }

            return new MediaInformationDto
            {
                RequestedUrl = query,
                StreamingUrl = videoData.GetProperty("url").GetString(),
                CoverUrl = thumbnailUrl,
                Title = videoData.GetProperty("title").GetString(),
                Author = videoData.GetProperty("uploader").GetString()
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