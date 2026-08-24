using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArgonFetch.Application.Services
{
    /// <summary>
    /// The track details ArgonFetch needs from a Spotify link. The audio itself never comes
    /// from Spotify - these fields only seed the YouTube Music search and fill the DTO.
    /// </summary>
    public record SpotifyTrackMetadata(string Title, string Artist, string? CoverUrl, long DurationMs);

    /// <summary>One entry of an album or playlist, with everything matching needs.</summary>
    public record SpotifyCollectionItem(string Title, string Artist, long DurationMs, string TrackUrl);

    /// <summary>
    /// An album or playlist and what is on it.
    /// </summary>
    /// <param name="MayBeTruncated">
    /// Whether the source stopped short of the whole thing. The page this is read from returns
    /// at most a hundred entries, and says nothing about how many it left out.
    /// </param>
    public record SpotifyCollectionMetadata(
        string Title,
        string? Author,
        string? CoverUrl,
        IReadOnlyList<SpotifyCollectionItem> Items,
        bool MayBeTruncated);

    public interface ISpotifyMetadataService
    {
        Task<SpotifyTrackMetadata> GetTrackAsync(string trackUrl, CancellationToken cancellationToken = default);

        /// <summary>
        /// The contents of an album or playlist page.
        /// </summary>
        Task<SpotifyCollectionMetadata> GetCollectionAsync(string collectionUrl, CancellationToken cancellationToken = default);
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
        // The embed page is a Next.js app; everything it renders is in this one script tag.
        private static readonly Regex NextData = new(
            "<script id=\"__NEXT_DATA__\" type=\"application/json\">(.*?)</script>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // What the embed page returns at most. A listing of exactly this many may have more
        // behind it that the page did not send.
        private const int EmbedTrackLimit = 100;

        private static readonly Regex PlaylistUrl = new(@"/playlist/([A-Za-z0-9]+)", RegexOptions.Compiled);

        private static readonly Regex DurationPattern = new("\"duration\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ISpotifyWebPlayerClient _webPlayerClient;
        private readonly ILogger<SpotifyMetadataService> _logger;

        public SpotifyMetadataService(
            IHttpClientFactory httpClientFactory,
            ISpotifyWebPlayerClient webPlayerClient,
            ILogger<SpotifyMetadataService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _webPlayerClient = webPlayerClient;
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
        /// <summary>
        /// Reads an album or playlist from the embed page.
        /// <para>
        /// The OpenGraph tags name the release but never list what is on it. The embed page
        /// carries the whole track list as JSON - title, artist and duration each - and needs no
        /// token, no persisted query and no session, which the Web API and its GraphQL endpoint
        /// all do.
        /// </para>
        /// </summary>
        public async Task<SpotifyCollectionMetadata> GetCollectionAsync(string collectionUrl, CancellationToken cancellationToken = default)
        {
            var httpClient = _httpClientFactory.CreateClient(MediaHttpClientDefaults.ClientName);

            using var response = await httpClient.GetAsync(EmbedUrl(collectionUrl), cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new ArgumentException(
                    $"Spotify returned {(int)response.StatusCode} for {collectionUrl}. It may not exist or may be private.");
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var entity = ReadEmbeddedEntity(html, collectionUrl);

            var items = new List<SpotifyCollectionItem>();

            // The embed page stops at a hundred, so a long playlist is read through the web
            // player's own API instead. That path can break in ways this one cannot - a rotated
            // query hash, a moved secret - so the short answer stays as the floor beneath it.
            var playlistId = PlaylistId(collectionUrl);

            if (playlistId is not null)
            {
                var full = await _webPlayerClient.TryGetPlaylistTracksAsync(playlistId, cancellationToken);

                if (full is { Count: > 0 })
                {
                    return new SpotifyCollectionMetadata(
                        Text(entity, "name") ?? Text(entity, "title") ?? "Unknown",
                        Text(entity, "subtitle"),
                        await TryGetCoverUrlAsync(collectionUrl, httpClient, cancellationToken),
                        full,
                        MayBeTruncated: false);
                }

                _logger.LogInformation(
                    "Falling back to the embed listing for {Url}, which returns at most {Limit} tracks.",
                    collectionUrl, EmbedTrackLimit);
            }

            if (entity.TryGetProperty("trackList", out var trackList) && trackList.ValueKind == JsonValueKind.Array)
            {
                foreach (var track in trackList.EnumerateArray())
                {
                    var uri = Text(track, "uri");
                    var id = uri?.Split(':').LastOrDefault();

                    // An entry with no id cannot be fetched later, so it is not worth listing.
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    items.Add(new SpotifyCollectionItem(
                        Text(track, "title") ?? "Unknown",
                        Text(track, "subtitle") ?? string.Empty,
                        track.TryGetProperty("duration", out var duration) && duration.TryGetInt64(out var ms) ? ms : 0L,
                        $"https://open.spotify.com/track/{id}"));
                }
            }

            if (items.Count == 0)
                throw new ArgumentException($"Spotify listed no tracks for {collectionUrl}.");

            // Cover art is absent from the embed payload but present in the OpenGraph tags on the
            // ordinary page, which is one more request rather than a different mechanism.
            var coverUrl = await TryGetCoverUrlAsync(collectionUrl, httpClient, cancellationToken);

            return new SpotifyCollectionMetadata(
                Text(entity, "name") ?? Text(entity, "title") ?? "Unknown",
                Text(entity, "subtitle"),
                coverUrl,
                items,
                items.Count == EmbedTrackLimit);
        }

        /// <summary>
        /// The entity object the embed page is rendered from, which holds the track list.
        /// </summary>
        private static JsonElement ReadEmbeddedEntity(string html, string collectionUrl)
        {
            var match = NextData.Match(html);

            if (!match.Success)
            {
                throw new ArgumentException(
                    $"Spotify's embed page for {collectionUrl} carried no data. Its markup may have changed.");
            }

            using var document = JsonDocument.Parse(match.Groups[1].Value);

            var entity = document.RootElement
                .GetProperty("props")
                .GetProperty("pageProps")
                .GetProperty("state")
                .GetProperty("data")
                .GetProperty("entity");

            // Cloned because the document is disposed on the way out of this method.
            return entity.Clone();
        }

        private static string? Text(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private async Task<string?> TryGetCoverUrlAsync(string collectionUrl, HttpClient httpClient, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await httpClient.GetAsync(collectionUrl, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return null;

                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var parser = new HtmlParser();
                using var document = await parser.ParseDocumentAsync(html, cancellationToken);

                return GetMetaContent(document, "og:image");
            }
            catch (Exception ex)
            {
                // A listing without its picture is still a listing.
                _logger.LogDebug(ex, "Could not read the cover art for {Url}", collectionUrl);
                return null;
            }
        }

        /// <summary>The playlist id in a url, or null when it is not a playlist link.</summary>
        private static string? PlaylistId(string url)
        {
            var match = PlaylistUrl.Match(url);

            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>The embed page for any Spotify entity url.</summary>
        private static string EmbedUrl(string url)
        {
            var normalized = url.Split('?')[0].TrimEnd('/');

            foreach (var kind in new[] { "playlist", "album", "track" })
            {
                var segment = $"/{kind}/";

                if (normalized.Contains(segment, StringComparison.OrdinalIgnoreCase))
                    return normalized.Replace(segment, $"/embed/{kind}/", StringComparison.OrdinalIgnoreCase);
            }

            throw new ArgumentException($"{url} is not a Spotify track, album or playlist link.");
        }

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
