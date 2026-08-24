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

        /// <summary>
        /// The caller's Range header, if it sent one. Honoured only where bytes are passed
        /// through: a conversion has no fixed length to seek within.
        /// </summary>
        public string? RangeHeader { get; }

        /// <summary>
        /// Container the caller insists on, currently only "mp3". Null serves the source
        /// untouched, which is faster and keeps the original quality.
        /// </summary>
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

                var (mediaUrl, isAudio, advertisedMimeType, proxy, tags) = cacheData.Value;

                // The fetch response already committed to a media type for this key. When it
                // knows one, those bytes go out untouched: re-encoding Opus into MP3 cost a
                // generation of quality, every tag the source carried, and the FFmpeg pass.
                var passThroughMimeType = advertisedMimeType ?? StandardFormatMimeType(mediaUrl, isAudio);

                // A caller can still ask for MP3 explicitly - players that cannot read Opus are
                // the reason the endpoint used to convert everything - but it is opt-in now.
                var mp3Requested = string.Equals(request.Format, "mp3", StringComparison.OrdinalIgnoreCase);
                bool needsConversion = passThroughMimeType == null || (isAudio && mp3Requested);

                if (needsConversion)
                {
                    _logger.LogInformation("Converting media from {Url} to {Format}",
                        mediaUrl, isAudio ? "MP3" : "MP4");

                    // Set response headers for converted format
                    request.Response.ContentType = isAudio ? "audio/mpeg" : "video/mp4";
                    request.Response.Headers.Append("Cache-Control", "public, max-age=3600");
                    request.Response.Headers.ContentDisposition =
                        MediaFileName.ContentDisposition(tags, isAudio ? ".mp3" : ".mp4");

                    // Stream and convert using FFmpeg. The conversion re-encodes anyway, so it
                    // is the one path that can write the title, artist and cover art into the
                    // file rather than only into its name.
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
                    // Source format, stream directly without conversion
                    _logger.LogInformation("Streaming {MimeType} media from {Url} using accelerated download via {Proxy}",
                        passThroughMimeType, mediaUrl, MediaHttpClients.Describe(proxy));

                    request.Response.ContentType = passThroughMimeType;

                    // Add cache headers
                    request.Response.Headers.Append("Cache-Control", "public, max-age=3600");

                    // Named, but not touched: writing tags into these bytes would mean remuxing
                    // them, which costs the exact length the response has already promised and
                    // the byte ranges a client may be asking for.
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

                        // Advertised only here, where it is true: the bytes come from a source
                        // that serves ranges and reach the caller untouched, so a seek can be
                        // answered exactly.
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

                    // The service already falls back to a single connection internally, and only
                    // while doing so is still safe. Retrying here would append a second copy of
                    // the file to a response body that may already hold part of one.
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

        /// <summary>
        /// The media type to serve a source with untouched, or null when it has to be converted.
        /// <para>
        /// Only reached for keys cached before the fetch response started recording the type,
        /// so it works the type out of the URL: media URLs carry a "mime" query parameter, and
        /// failing that an extension is sometimes recoverable from the path.
        /// </para>
        /// </summary>
        private string? StandardFormatMimeType(string url, bool isAudio)
        {
            var mimeType = GetMimeType(url);

            if (mimeType != null)
            {
                // "audio/webm; codecs=opus" and friends: the parameters are not ours to forward.
                var bare = mimeType.Split(';')[0].Trim();

                if (bare.StartsWith(isAudio ? "audio/" : "video/", StringComparison.OrdinalIgnoreCase))
                    return bare;
            }

            return MediaFormats.MimeTypeFor(GetFileExtension(url), isAudio);
        }

        /// <summary>
        /// The source's media type. Media URLs carry it as a "mime" query parameter
        /// (e.g. mime=video%2Fmp4) even though the path has no file extension.
        /// </summary>
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