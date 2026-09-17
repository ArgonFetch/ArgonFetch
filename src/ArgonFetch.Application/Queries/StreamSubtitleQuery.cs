using ArgonFetch.Application.Services;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace ArgonFetch.Application.Queries
{
    /// <summary>
    /// Serves one subtitle track as WebVTT.
    /// </summary>
    /// <remarks>
    /// The instance fetches it rather than pointing a client at the source. YouTube signs those
    /// URLs for the player that asked and answers a browser with <c>200</c> and nothing in it, so
    /// a client that follows the link gets a track with no cues and no error to explain it.
    /// </remarks>
    public class StreamSubtitleQuery : IRequest<StreamResult>
    {
        public StreamSubtitleQuery(string key, HttpResponse response, CancellationToken cancellationToken)
        {
            Key = key;
            Response = response;
            CancellationToken = cancellationToken;
        }

        public string Key { get; }
        public HttpResponse Response { get; }
        public CancellationToken CancellationToken { get; }
    }

    public class StreamSubtitleQueryHandler : IRequestHandler<StreamSubtitleQuery, StreamResult>
    {
        private readonly IMediaUrlCacheService _cacheService;
        private readonly IMediaHttpClients _httpClients;
        private readonly ILogger<StreamSubtitleQueryHandler> _logger;

        public StreamSubtitleQueryHandler(
            IMediaUrlCacheService cacheService,
            IMediaHttpClients httpClients,
            ILogger<StreamSubtitleQueryHandler> logger)
        {
            _cacheService = cacheService;
            _httpClients = httpClients;
            _logger = logger;
        }

        public async ValueTask<StreamResult> Handle(StreamSubtitleQuery request, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Key))
                    return StreamResult.BadRequest("Cache key is required");

                var cached = _cacheService.GetCachedUrlWithFormat(request.Key);

                if (cached is null)
                    return StreamResult.NotFound("Cache key expired or not found");

                var (url, _, _, proxy, _) = cached.Value;

                var client = _httpClients.For(proxy);
                var body = await client.GetStringAsync(url, request.CancellationToken);

                if (string.IsNullOrWhiteSpace(body))
                {
                    _logger.LogWarning("Subtitle source returned nothing for {Url}", url);
                    return StreamResult.BadGateway("The source returned an empty subtitle track");
                }

                var vtt = AsWebVtt(body);

                request.Response.ContentType = "text/vtt; charset=utf-8";
                request.Response.Headers.Append("Cache-Control", "public, max-age=3600");

                await request.Response.WriteAsync(vtt, Encoding.UTF8, request.CancellationToken);

                return StreamResult.Success();
            }
            catch (OperationCanceledException)
            {
                return StreamResult.ClientDisconnected();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Could not fetch the subtitle track");
                return StreamResult.BadGateway("Failed to fetch the subtitle track");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error serving a subtitle track");
                return StreamResult.ServerError("An unexpected error occurred serving subtitles");
            }
        }

        /// <summary>
        /// Whatever the source sent, as WebVTT. Sources answer in three shapes and a media element
        /// reads only one of them.
        /// </summary>
        internal static string AsWebVtt(string body)
        {
            var text = body.TrimStart('﻿', ' ', '\n', '\r', '\t');

            if (text.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
                return body;

            if (text.StartsWith('{'))
                return Json3ToVtt(text) ?? body;

            if (text.StartsWith('<'))
                return XmlToVtt(text) ?? body;

            return body;
        }

        /// <summary>{"events":[{"tStartMs":0,"dDurationMs":1200,"segs":[{"utf8":"..."}]}]}</summary>
        private static string? Json3ToVtt(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);

                if (!document.RootElement.TryGetProperty("events", out var events))
                    return null;

                var builder = new StringBuilder("WEBVTT\n\n");
                var wrote = false;

                foreach (var moment in events.EnumerateArray())
                {
                    if (!moment.TryGetProperty("tStartMs", out var startMs))
                        continue;

                    var segments = moment.TryGetProperty("segs", out var segs)
                        ? string.Concat(segs.EnumerateArray()
                            .Select(seg => seg.TryGetProperty("utf8", out var utf8) ? utf8.GetString() : null))
                        : null;

                    if (string.IsNullOrWhiteSpace(segments))
                        continue;

                    var from = startMs.GetDouble() / 1000;
                    var length = moment.TryGetProperty("dDurationMs", out var durationMs)
                        ? durationMs.GetDouble() / 1000
                        : 2;

                    builder.Append(Timestamp(from)).Append(" --> ").Append(Timestamp(from + length)).Append('\n');
                    builder.Append(segments.Trim()).Append("\n\n");

                    wrote = true;
                }

                return wrote ? builder.ToString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>&lt;transcript&gt;&lt;text start="0" dur="1.5"&gt;…&lt;/text&gt;&lt;/transcript&gt;</summary>
        private static string? XmlToVtt(string body)
        {
            try
            {
                var document = XDocument.Parse(body);
                var lines = document.Descendants("text").ToList();

                if (lines.Count == 0)
                    return null;

                var builder = new StringBuilder("WEBVTT\n\n");
                var wrote = false;

                foreach (var line in lines)
                {
                    if (!double.TryParse(line.Attribute("start")?.Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var from))
                        continue;

                    if (!double.TryParse(line.Attribute("dur")?.Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var length))
                        length = 2;

                    // XDocument already turned one layer of escaping back into text; the payload
                    // carried two, so the value still reads as markup at this point.
                    var said = System.Net.WebUtility.HtmlDecode(line.Value).Trim();

                    if (said.Length == 0)
                        continue;

                    builder.Append(Timestamp(from)).Append(" --> ").Append(Timestamp(from + length)).Append('\n');
                    builder.Append(said).Append("\n\n");

                    wrote = true;
                }

                return wrote ? builder.ToString() : null;
            }
            catch (System.Xml.XmlException)
            {
                return null;
            }
        }

        private static string Timestamp(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));

            return span.ToString(@"hh\:mm\:ss\.fff", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
