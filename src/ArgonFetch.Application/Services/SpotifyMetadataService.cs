using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace ArgonFetch.Application.Services
{
    /// <summary>
    /// The track details ArgonFetch needs from a Spotify link. The audio itself never comes
    /// from Spotify - these fields only seed the YouTube Music search and fill the DTO.
    /// </summary>
    public record SpotifyTrackMetadata(string Title, string Artist, string? CoverUrl, long DurationMs);

    public interface ISpotifyMetadataService
    {
        Task<SpotifyTrackMetadata> GetTrackAsync(string trackUrl, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reads Spotify track details from the OpenGraph tags on the public track page.
    /// <para>
    /// This replaces the authenticated Web API client. Only the title, artist and cover art
    /// were ever used, and all three are served unauthenticated, so requiring client
    /// credentials to reach them meant Spotify links did not work out of the box.
    /// </para>
    /// </summary>
    public class SpotifyMetadataService : ISpotifyMetadataService
    {
        private static readonly Regex DurationPattern = new("\"duration\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SpotifyMetadataService> _logger;

        public SpotifyMetadataService(IHttpClientFactory httpClientFactory, ILogger<SpotifyMetadataService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<SpotifyTrackMetadata> GetTrackAsync(string trackUrl, CancellationToken cancellationToken = default)
        {
            var requestUrl = NormalizeTrackUrl(trackUrl);

            var httpClient = _httpClientFactory.CreateClient(MediaHttpClientDefaults.ClientName);

            using var response = await httpClient.GetAsync(requestUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new ArgumentException(
                    $"Spotify returned {(int)response.StatusCode} for {requestUrl}. The track may not exist or may not be available.");
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            var parser = new HtmlParser();
            using var document = await parser.ParseDocumentAsync(html, cancellationToken);

            var title = GetMetaContent(document, "og:title");
            var description = GetMetaContent(document, "og:description");
            var coverUrl = GetMetaContent(document, "og:image");

            if (string.IsNullOrWhiteSpace(title))
            {
                // A track page always carries og:title. Its absence means a consent wall,
                // a redirect, or a layout change rather than an empty title.
                throw new ArgumentException($"Could not read track details from {requestUrl}.");
            }

            var artist = ParseArtist(description);

            if (string.IsNullOrWhiteSpace(artist))
            {
                _logger.LogWarning(
                    "Spotify page for {Url} had no artist in og:description ({Description}); searching by title alone",
                    requestUrl, description);
            }

            // Duration is not in the OpenGraph tags, but the embed page carries it and needs no
            // auth either. It lets the matcher reject same-title recordings of the wrong length,
            // so it is worth the extra request - a failure here just weakens matching.
            var durationMs = await TryGetDurationMsAsync(requestUrl, httpClient, cancellationToken);

            return new SpotifyTrackMetadata(title, artist ?? string.Empty, coverUrl, durationMs);
        }

        private async Task<long> TryGetDurationMsAsync(
            string trackUrl,
            HttpClient httpClient,
            CancellationToken cancellationToken)
        {
            try
            {
                var embedUrl = trackUrl.Replace("/track/", "/embed/track/", StringComparison.Ordinal);

                using var response = await httpClient.GetAsync(embedUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return 0;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                var match = DurationPattern.Match(body);
                return match.Success && long.TryParse(match.Groups[1].Value, out var ms) ? ms : 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not read duration for {Url}; matching will skip the duration check", trackUrl);
                return 0;
            }
        }

        /// <summary>
        /// og:description is a middot-separated list, e.g.
        /// "Rick Astley · Whenever You Need Somebody · Song · 1987", where the artist comes first.
        /// </summary>
        private static string? ParseArtist(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            var artist = description.Split('·', StringSplitOptions.TrimEntries)[0];

            return string.IsNullOrWhiteSpace(artist) ? null : artist;
        }

        private static string? GetMetaContent(AngleSharp.Dom.IDocument document, string property)
        {
            var content = document
                .QuerySelector($"meta[property='{property}']")?
                .GetAttribute("content");

            return string.IsNullOrWhiteSpace(content) ? null : content;
        }

        /// <summary>
        /// Accepts the URL forms users actually paste - localized paths like /intl-de/track/{id},
        /// tracking query strings, and spotify:track:{id} URIs - and reduces them to the
        /// canonical track page.
        /// </summary>
        private static string NormalizeTrackUrl(string trackUrl)
        {
            if (string.IsNullOrWhiteSpace(trackUrl))
            {
                throw new ArgumentException("Spotify URL must not be empty.", nameof(trackUrl));
            }

            var trimmed = trackUrl.Trim();

            if (trimmed.StartsWith("spotify:track:", StringComparison.OrdinalIgnoreCase))
            {
                var uriId = trimmed["spotify:track:".Length..];
                return $"https://open.spotify.com/track/{uriId}";
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException($"'{trackUrl}' is not a valid Spotify URL.", nameof(trackUrl));
            }

            if (!uri.Host.EndsWith("spotify.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"'{trackUrl}' is not a Spotify URL.", nameof(trackUrl));
            }

            var segments = uri.Segments
                .Select(s => s.Trim('/'))
                .Where(s => s.Length > 0)
                .ToArray();

            var trackIndex = Array.FindIndex(segments, s => s.Equals("track", StringComparison.OrdinalIgnoreCase));

            if (trackIndex < 0 || trackIndex + 1 >= segments.Length)
            {
                throw new ArgumentException(
                    $"'{trackUrl}' does not look like a Spotify track link.", nameof(trackUrl));
            }

            // Drop query strings; they carry share/tracking parameters, not identity.
            return $"https://open.spotify.com/track/{segments[trackIndex + 1]}";
        }
    }
}
