using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Services;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Application.Queries
{
    public class StreamCombinedMediaQuery : IRequest<StreamResult>
    {
        public StreamCombinedMediaQuery(string key, HttpResponse response, CancellationToken cancellationToken)
        {
            Key = key;
            Response = response;
            CancellationToken = cancellationToken;
        }

        public string Key { get; }
        public HttpResponse Response { get; }
        public CancellationToken CancellationToken { get; }
    }

    public class StreamCombinedMediaQueryHandler : IRequestHandler<StreamCombinedMediaQuery, StreamResult>
    {
        private readonly IFfmpegStreamingService _ffmpegStreamingService;
        private readonly IMediaUrlCacheService _cacheService;
        private readonly ILogger<StreamCombinedMediaQueryHandler> _logger;

        public StreamCombinedMediaQueryHandler(
            IFfmpegStreamingService ffmpegStreamingService,
            IMediaUrlCacheService cacheService,
            ILogger<StreamCombinedMediaQueryHandler> logger)
        {
            _ffmpegStreamingService = ffmpegStreamingService;
            _cacheService = cacheService;
            _logger = logger;
        }

        public async Task<StreamResult> Handle(StreamCombinedMediaQuery request, CancellationToken cancellationToken)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(request.Key))
                {
                    return StreamResult.BadRequest("Cache key is required");
                }

                // Get URLs from cache
                var (actualVideoUrl, actualAudioUrl) = _cacheService.GetCachedUrls(request.Key);

                if (actualVideoUrl == null || actualAudioUrl == null)
                {
                    return StreamResult.NotFound("Cache key expired or not found");
                }

                // Set response headers before starting stream
                request.Response.ContentType = "video/mp4";
                request.Response.Headers.Append("Content-Disposition", "inline; filename=\"video.mp4\"");
                request.Response.Headers.Append("Cache-Control", "no-cache");

                // Start streaming - once this starts, we cannot change headers
                await _ffmpegStreamingService.StreamCombinedMediaAsync(
                    actualVideoUrl,
                    actualAudioUrl,
                    request.Response.Body,
                    request.CancellationToken);

                return StreamResult.Success();
            }
            catch (OperationCanceledException)
            {
                // Client disconnected, this is normal
                _logger.LogInformation("Client disconnected during streaming");
                return StreamResult.ClientDisconnected();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "FFmpeg not found");
                return StreamResult.ServerError("FFmpeg is required for streaming combined media");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming combined media");
                return StreamResult.BadGateway("Failed to stream combined media");
            }
        }
    }

    public class StreamResult
    {
        public bool IsSuccess { get; private set; }
        public int? StatusCode { get; private set; }
        public string? ErrorMessage { get; private set; }
        public bool IsClientDisconnected { get; private set; }

        private StreamResult() { }

        public static StreamResult Success()
        {
            return new StreamResult { IsSuccess = true };
        }

        public static StreamResult BadRequest(string message)
        {
            return new StreamResult
            {
                IsSuccess = false,
                StatusCode = StatusCodes.Status400BadRequest,
                ErrorMessage = message
            };
        }

        public static StreamResult NotFound(string message)
        {
            return new StreamResult
            {
                IsSuccess = false,
                StatusCode = StatusCodes.Status404NotFound,
                ErrorMessage = message
            };
        }

        public static StreamResult ServerError(string message)
        {
            return new StreamResult
            {
                IsSuccess = false,
                StatusCode = StatusCodes.Status500InternalServerError,
                ErrorMessage = message
            };
        }

        public static StreamResult BadGateway(string message)
        {
            return new StreamResult
            {
                IsSuccess = false,
                StatusCode = StatusCodes.Status502BadGateway,
                ErrorMessage = message
            };
        }

        public static StreamResult ClientDisconnected()
        {
            return new StreamResult
            {
                IsSuccess = true,
                IsClientDisconnected = true
            };
        }
    }
}