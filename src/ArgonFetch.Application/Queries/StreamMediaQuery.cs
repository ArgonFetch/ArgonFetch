using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Services;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace ArgonFetch.Application.Queries
{
    public class StreamMediaQuery : IRequest<StreamResult>
    {
        public StreamMediaQuery(string key, HttpResponse response, CancellationToken cancellationToken)
        {
            Key = key;
            Response = response;
            CancellationToken = cancellationToken;
        }

        public string Key { get; }
        public HttpResponse Response { get; }
        public CancellationToken CancellationToken { get; }
    }

    public class StreamMediaQueryHandler : IRequestHandler<StreamMediaQuery, StreamResult>
    {
        private readonly IMediaUrlCacheService _cacheService;
        private readonly IFfmpegStreamingService _ffmpegStreamingService;
        private readonly IAcceleratedDownloadService _acceleratedDownloadService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<StreamMediaQueryHandler> _logger;

        public StreamMediaQueryHandler(
            IMediaUrlCacheService cacheService,
            IFfmpegStreamingService ffmpegStreamingService,
            IAcceleratedDownloadService acceleratedDownloadService,
            IHttpClientFactory httpClientFactory,
            ILogger<StreamMediaQueryHandler> logger)
        {
            _cacheService = cacheService;
            _ffmpegStreamingService = ffmpegStreamingService;
            _acceleratedDownloadService = acceleratedDownloadService;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<StreamResult> Handle(StreamMediaQuery request, CancellationToken cancellationToken)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(request.Key))
                {
                    return StreamResult.BadRequest("Cache key is required");
                }

                // Get URL and format info from cache
                var cacheData = _cacheService.GetCachedUrlWithFormat(request.Key);

                if (cacheData == null)
                {
                    return StreamResult.NotFound("Cache key expired or not found");
                }

                var (mediaUrl, isAudio) = cacheData.Value;

                // Determine if conversion is needed based on URL extension
                bool needsConversion = !IsStandardFormat(mediaUrl, isAudio);

                if (needsConversion)
                {
                    _logger.LogInformation("Converting media from {Url} to {Format}",
                        mediaUrl, isAudio ? "MP3" : "MP4");

                    // Set response headers for converted format
                    request.Response.ContentType = isAudio ? "audio/mpeg" : "video/mp4";
                    request.Response.Headers.Append("Cache-Control", "public, max-age=3600");

                    // Stream and convert using FFmpeg
                    await _ffmpegStreamingService.ConvertAndStreamMediaAsync(
                        mediaUrl,
                        request.Response.Body,
                        isAudio,
                        request.CancellationToken);
                }
                else
                {
                    // Standard format, stream directly without conversion
                    _logger.LogInformation("Streaming standard format media from {Url} using accelerated download", mediaUrl);

                    // Set proper content type
                    if (isAudio)
                    {
                        request.Response.ContentType = "audio/mpeg";
                    }
                    else
                    {
                        request.Response.ContentType = "video/mp4";
                    }

                    // Add cache headers
                    request.Response.Headers.Append("Cache-Control", "public, max-age=3600");

                    // Note: We don't set Content-Length as we might be chunking
                    // The accelerated download will handle range requests if supported

                    try
                    {
                        // Use accelerated download service for better speed
                        await _acceleratedDownloadService.StreamWithAccelerationAsync(
                            mediaUrl,
                            request.Response.Body,
                            null, // No progress reporting needed here
                            request.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Accelerated download failed, falling back to single connection");

                        // Fallback to single connection if accelerated fails
                        var httpClient = _httpClientFactory.CreateClient();
                        httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                        using var response = await httpClient.GetAsync(
                            mediaUrl,
                            HttpCompletionOption.ResponseHeadersRead,
                            request.CancellationToken);

                        if (!response.IsSuccessStatusCode)
                        {
                            return StreamResult.BadGateway($"Failed to fetch media: {response.ReasonPhrase}");
                        }

                        await response.Content.CopyToAsync(request.Response.Body, request.CancellationToken);
                    }
                }

                return StreamResult.Success();
            }
            catch (OperationCanceledException)
            {
                // Client disconnected, this is normal
                _logger.LogInformation("Client disconnected during media streaming");
                return StreamResult.ClientDisconnected();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("FFmpeg"))
            {
                _logger.LogError(ex, "FFmpeg error during conversion");
                return StreamResult.ServerError("Media conversion failed");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error while fetching media");
                return StreamResult.BadGateway("Failed to fetch media from source");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error streaming media");
                return StreamResult.ServerError("An unexpected error occurred while streaming media");
            }
        }

        private bool IsStandardFormat(string url, bool isAudio)
        {
            var extension = GetFileExtension(url);

            if (isAudio)
            {
                // Only MP3 is considered standard for audio
                return extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                // Only MP4 is considered standard for video
                return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
            }
        }

        private string GetFileExtension(string url)
        {
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath;
                var extension = Path.GetExtension(path);

                // If no extension found in path, try to extract from query parameters
                if (string.IsNullOrEmpty(extension))
                {
                    // Check for common patterns like "format=mp4" or "ext=mp3"
                    var query = uri.Query.ToLower();
                    if (query.Contains("format=mp4") || query.Contains("ext=mp4"))
                        return ".mp4";
                    if (query.Contains("format=mp3") || query.Contains("ext=mp3"))
                        return ".mp3";
                    if (query.Contains("format=webm") || query.Contains("ext=webm"))
                        return ".webm";
                    if (query.Contains("format=m4a") || query.Contains("ext=m4a"))
                        return ".m4a";
                }

                return extension;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}