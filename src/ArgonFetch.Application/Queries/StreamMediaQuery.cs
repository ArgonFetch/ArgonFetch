using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Services;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace ArgonFetch.Application.Queries
{
    public class StreamMediaQuery : IRequest<StreamResult>
    {
        public StreamMediaQuery(
            string key,
            HttpResponse response,
            CancellationToken cancellationToken,
            string? format = null,
            string? rangeHeader = null)
        {
            Key = key;
            Response = response;
            CancellationToken = cancellationToken;
            Format = format;
            RangeHeader = rangeHeader;
        }

        public string? RangeHeader { get; }

        public string? Format { get; }

        public string Key { get; }
        public HttpResponse Response { get; }
        public CancellationToken CancellationToken { get; }
    }

    public class StreamMediaQueryHandler : IRequestHandler<StreamMediaQuery, StreamResult>
    {
        private readonly IMediaUrlCacheService _cacheService;
        private readonly IFfmpegStreamingService _ffmpegStreamingService;
        private readonly IAcceleratedDownloadService _acceleratedDownloadService;
        private readonly ILogger<StreamMediaQueryHandler> _logger;

        public StreamMediaQueryHandler(
            IMediaUrlCacheService cacheService,
            IFfmpegStreamingService ffmpegStreamingService,
            IAcceleratedDownloadService acceleratedDownloadService,
            ILogger<StreamMediaQueryHandler> logger)
        {
            _cacheService = cacheService;
            _ffmpegStreamingService = ffmpegStreamingService;
            _acceleratedDownloadService = acceleratedDownloadService;
            _logger = logger;
        }

        public async ValueTask<StreamResult> Handle(StreamMediaQuery request, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Key))
                {
                    return StreamResult.BadRequest("Cache key is required");
                }

                var cacheData = _cacheService.GetCachedUrlWithFormat(request.Key);

                if (cacheData == null)
                {
                    return StreamResult.NotFound("Cache key expired or not found");
                }

                var (mediaUrl, isAudio, advertisedMimeType, proxy, tags) = cacheData.Value;

                var passThroughMimeType = advertisedMimeType ?? StandardFormatMimeType(mediaUrl, isAudio);

                var mp3Requested = string.Equals(request.Format, "mp3", StringComparison.OrdinalIgnoreCase);
                bool needsConversion = passThroughMimeType == null || (isAudio && mp3Requested);

                if (needsConversion)
                {
                    _logger.LogInformation("Converting media from {Url} to {Format}",
                        mediaUrl, isAudio ? "MP3" : "MP4");

                    request.Response.ContentType = isAudio ? "audio/mpeg" : "video/mp4";
                    request.Response.Headers.Append("Cache-Control", "public, max-age=3600");
                    request.Response.Headers.ContentDisposition =
                        MediaFileName.ContentDisposition(tags, isAudio ? ".mp3" : ".mp4");

                    await _ffmpegStreamingService.ConvertAndStreamMediaAsync(
                        mediaUrl,
                        request.Response.Body,
                        isAudio,
                        proxy,
                        tags,
                        request.CancellationToken);
                }
                else
                {
                    _logger.LogInformation("Streaming {MimeType} media from {Url} using accelerated download via {Proxy}",
                        passThroughMimeType, mediaUrl, MediaHttpClients.Describe(proxy));

                    request.Response.ContentType = passThroughMimeType;

                    request.Response.Headers.Append("Cache-Control", "public, max-age=3600");

                    request.Response.Headers.ContentDisposition = MediaFileName.ContentDisposition(
                        tags,
                        MediaFormats.ExtensionFor(passThroughMimeType) ?? (isAudio ? ".mp3" : ".mp4"));

                    // This path copies the upstream bytes through unchanged, so the upstream
                    // length is the response length and can be declared. Without it the client
                    // gets no Content-Length and cannot show real download progress.
                    //
                    // Only declared when the probe returns a length: whatever is written must
                    // match it exactly or Kestrel throws on response completion. Conversion is
                    // handled in the branch above, where the output length is not knowable.
                    var upstreamLength = await _acceleratedDownloadService.GetContentLengthAsync(
                        mediaUrl,
                        proxy,
                        request.CancellationToken);

                    ByteRange? window = null;

                    if (upstreamLength.HasValue)
                    {
                        var total = upstreamLength.Value;

                        request.Response.Headers.AcceptRanges = "bytes";

                        switch (Services.RangeHeader.Parse(request.RangeHeader, total, out var requested))
                        {
                            case RangeRequest.Satisfiable:
                                window = requested;
                                request.Response.StatusCode = StatusCodes.Status206PartialContent;
                                request.Response.Headers.ContentRange = $"bytes {requested.From}-{requested.To}/{total}";
                                request.Response.ContentLength = requested.Length;

                                _logger.LogInformation("Serving bytes {From}-{To} of {Total} for {Url}",
                                    requested.From, requested.To, total, mediaUrl);
                                break;

                            case RangeRequest.Unsatisfiable:
                                // Nothing of the resource lies in the asked-for window, so the
                                // caller is told how long it actually is and given no body.
                                request.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                                request.Response.Headers.ContentRange = $"bytes */{total}";
                                request.Response.ContentLength = 0;

                                return StreamResult.Success();

                            default:
                                request.Response.ContentLength = total;
                                _logger.LogInformation("Declared Content-Length {Length} for {Url}", total, mediaUrl);
                                break;
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Upstream did not report a length for {Url}; response will be chunked", mediaUrl);
                    }

                    await _acceleratedDownloadService.StreamWithAccelerationAsync(
                        mediaUrl,
                        request.Response.Body,
                        null, // No progress reporting needed here
                        proxy,
                        window,
                        request.CancellationToken);
                }

                return StreamResult.Success();
            }
            catch (OperationCanceledException)
            {
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

        private string? StandardFormatMimeType(string url, bool isAudio)
        {
            var mimeType = GetMimeType(url);

            if (mimeType != null)
            {
                var bare = mimeType.Split(';')[0].Trim();

                if (bare.StartsWith(isAudio ? "audio/" : "video/", StringComparison.OrdinalIgnoreCase))
                    return bare;
            }

            return MediaFormats.MimeTypeFor(GetFileExtension(url), isAudio);
        }

        private string? GetMimeType(string url)
        {
            try
            {
                var query = new Uri(url).Query;

                if (string.IsNullOrEmpty(query))
                {
                    return null;
                }

                var mime = Microsoft.AspNetCore.WebUtilities.QueryHelpers
                    .ParseQuery(query)
                    .TryGetValue("mime", out var values) ? values.ToString() : null;

                return string.IsNullOrWhiteSpace(mime) ? null : mime.Trim().ToLowerInvariant();
            }
            catch
            {
                return null;
            }
        }

        private string GetFileExtension(string url)
        {
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath;
                var extension = Path.GetExtension(path);

                if (string.IsNullOrEmpty(extension))
                {
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