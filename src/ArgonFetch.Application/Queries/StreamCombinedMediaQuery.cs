using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Services;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace ArgonFetch.Application.Queries
{
    public class StreamCombinedMediaQuery : IRequest<StreamResult>
    {
        public StreamCombinedMediaQuery(string key, HttpResponse response, CancellationToken cancellationToken, double startSeconds = 0)
        {
            Key = key;
            Response = response;
            CancellationToken = cancellationToken;
            StartSeconds = startSeconds;
        }

        public string Key { get; }

        /// <summary>Where the mux should begin, so a player can seek a stream that has no length.</summary>
        public double StartSeconds { get; }
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

        public async ValueTask<StreamResult> Handle(StreamCombinedMediaQuery request, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Key))
                {
                    return StreamResult.BadRequest("Cache key is required");
                }

                var (actualVideoUrl, actualAudioUrl, proxy, tags) = _cacheService.GetCachedUrls(request.Key);

                if (actualVideoUrl == null || actualAudioUrl == null)
                {
                    return StreamResult.NotFound("Cache key expired or not found");
                }

                request.Response.ContentType = "video/mp4";
                request.Response.Headers.ContentDisposition = MediaFileName.ContentDisposition(tags, ".mp4");
                request.Response.Headers.Append("Cache-Control", "no-cache");

                // Said out loud so a client can tell a seek took effect, and so a proxy does not
                // treat two different offsets as the same response.
                if (request.StartSeconds > 0)
                    request.Response.Headers.Append("X-Argon-Start", request.StartSeconds.ToString("0.###", CultureInfo.InvariantCulture));

                await _ffmpegStreamingService.StreamCombinedMediaAsync(
                    actualVideoUrl,
                    actualAudioUrl,
                    request.Response.Body,
                    proxy,
                    tags,
                    request.StartSeconds,
                    request.CancellationToken);

                return StreamResult.Success();
            }
            catch (OperationCanceledException)
            {
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