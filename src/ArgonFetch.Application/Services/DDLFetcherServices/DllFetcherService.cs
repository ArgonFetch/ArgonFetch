using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Models;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace ArgonFetch.Application.Services.DDLFetcherServices
{
    public class DllFetcherService : IDllFetcher
    {
        private readonly YoutubeDL _youtubeDL;

        public DllFetcherService()
        {
            _youtubeDL = new YoutubeDL();
            _youtubeDL.YoutubeDLPath = "yt-dlp"; // Ensure yt-dlp is installed
            _youtubeDL.FFmpegPath = "ffmpeg"; // Ensure ffmpeg is installed
        }

        public async Task<MediaInformationDto> FetchLinkAsync(string dllName, DllFetcherOptions dllFetcherOptions, CancellationToken cancellationToken)
        {
            var options = new OptionSet
            {
                Format = GetFormatString(dllFetcherOptions.MediaFormat),
                NoPlaylist = true,
                //ExtractAudio = dllFetcherOptions.MediaFormat == MediaFormat.BestAudio ||
                //              dllFetcherOptions.MediaFormat == MediaFormat.WorstAudio
            };

            var result = await _youtubeDL.RunVideoDataFetch(dllName, overrideOptions: options, ct: cancellationToken);
            if (!result.Success)
            {
                throw new Exception($"Failed to fetch video data: {string.Join(", ", result.ErrorOutput)}");
            }

            var videoData = result.Data;
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
                RequestedUrl = dllName,
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
