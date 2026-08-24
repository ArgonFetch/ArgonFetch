using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArgonFetch.Application.Services
{
    public interface ISpotifyWebPlayerClient
    {
        /// <summary>
        /// Every track on a playlist, or null when the web player's own API could not be reached.
        /// Null is a normal outcome the caller is expected to handle, not a fault.
        /// </summary>
        Task<IReadOnlyList<SpotifyCollectionItem>?> TryGetPlaylistTracksAsync(string playlistId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reads playlists through the same API the Spotify web player uses.
    /// <para>
    /// The embed page carries at most a hundred entries and says nothing about what it left out,
    /// so a 2,500-track playlist silently became a 100-track one. This asks the player's own
    /// endpoint instead, which pages to the end.
    /// </para>
    /// <para>
    /// No account and no credentials: the token minted here is anonymous, which is all that
    /// reading a public playlist needs. Nothing is hardcoded either - the one-time-password
    /// secret and the query hash both rotate, so both are fetched and cached rather than
    /// embedded and left to go stale.
    /// </para>
    /// </summary>
    public class SpotifyWebPlayerClient : ISpotifyWebPlayerClient
    {
        // A mirror that tracks the rotating TOTP secrets. Hosted on Gitea rather than GitHub
        // because the GitHub copy this used to read has since been taken down - which is also
        // the argument against pinning a secret into this file.
        private const string SecretsUrl = "https://code.thetadev.de/ThetaDev/spotify-secrets/raw/branch/main/secrets/secretDict.json";
        private const string ServerTimeUrl = "https://open.spotify.com/api/server-time";
        private const string WebPlayerUrl = "https://open.spotify.com/";
        private const string PathfinderUrl = "https://api-partner.spotify.com/pathfinder/v2/query";

        private const string TokenCacheKey = "spotify-web-player-token";
        private const string SecretsCacheKey = "spotify-web-player-secrets";
        private const string HashCacheKey = "spotify-web-player-hash";

        // The player itself asks for a hundred at a time.
        private const int PageSize = 100;

        // A stop for a playlist that is either enormous or a response that never stops paging.
        private const int MaxTracks = 10_000;

        // Enough to cover the handful of scripts the page loads, without turning a changed
        // page into a download of everything it mentions.
        private const int MaxBundlesToSearch = 5;

        // Every script the page pulls in. Which of them holds the query depends on how the
        // bundle was split that day, so they are tried in turn rather than guessed at.
        private static readonly Regex BundleUrls = new(
            @"https://open\.spotifycdn\.com/cdn/build/web-player/[^""'\s]+\.js",
            RegexOptions.Compiled);

        // The player itself, rather than the shorter agent used for media hosts: a request
        // that does not look like a desktop browser is served the mobile page, which is laid
        // out differently and carries no bundle this can read.
        private const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/144.0.0.0 Safari/537.36";

        // Persisted queries appear in the bundle as the operation name, the word "query", and the
        // hash the server will accept for it.
        private static readonly Regex PersistedQuery = new(
            @"""(\w+)"",""query"",""([0-9a-f]{64})""",
            RegexOptions.Compiled);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SpotifyWebPlayerClient> _logger;

        public SpotifyWebPlayerClient(
            IHttpClientFactory httpClientFactory,
            IMemoryCache cache,
            ILogger<SpotifyWebPlayerClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _logger = logger;
        }

        public async Task<IReadOnlyList<SpotifyCollectionItem>?> TryGetPlaylistTracksAsync(
            string playlistId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var token = await GetAccessTokenAsync(cancellationToken);
                var hash = await GetFetchPlaylistHashAsync(cancellationToken);

                var tracks = new List<SpotifyCollectionItem>();
                var total = int.MaxValue;
                var repaired = false;

                while (tracks.Count < total && tracks.Count < MaxTracks)
                {
                    var page = await FetchPageAsync(playlistId, tracks.Count, token, hash, cancellationToken);

                    // The session is held until something says otherwise. A long playlist can
                    // outlive its token, so one refusal buys one fresh token and one retry -
                    // never a loop, which is how a rejected session turns into a mint storm.
                    if (page is { Rejected: true } && !repaired)
                    {
                        repaired = true;
                        token = await GetAccessTokenAsync(cancellationToken, forceRefresh: true);
                        continue;
                    }

                    if (page is null or { Rejected: true })
                        return tracks.Count > 0 ? tracks : null;

                    total = page.Total;
                    tracks.AddRange(page.Items);

                    // A page that returns nothing would otherwise loop until the cap.
                    if (page.Items.Count == 0)
                        break;
                }

                _logger.LogInformation("Read {Count} of {Total} tracks for playlist {Id}", tracks.Count, total, playlistId);

                return tracks;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Any of it can break - the secret mirror, the bundle's shape, the query hash -
                // and the caller has a smaller answer it can fall back to.
                _logger.LogWarning(ex, "Could not read playlist {Id} through the web player API", playlistId);
                return null;
            }
        }

        private async Task<PlaylistPage?> FetchPageAsync(
            string playlistId,
            int offset,
            string token,
            string hash,
            CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(new
            {
                variables = new
                {
                    uri = $"spotify:playlist:{playlistId}",
                    offset,
                    limit = PageSize,
                    enableWatchFeedEntrypoint = false
                },
                operationName = "fetchPlaylist",
                extensions = new { persistedQuery = new { version = 1, sha256Hash = hash } }
            });

            var httpClient = _httpClientFactory.CreateClient(MediaHttpClientDefaults.ClientName);

            using var request = new HttpRequestMessage(HttpMethod.Post, PathfinderUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            // The bearer token alone: the player also sends a client-token, but the API does not
            // ask for one, and it is the single value with no runtime source to read it from.
            request.Headers.TryAddWithoutValidation("authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("accept", "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogDebug("Spotify refused the session at offset {Offset}; refreshing it once.", offset);
                return PlaylistPage.SessionRejected;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Spotify's playlist API answered {Status} at offset {Offset}", (int)response.StatusCode, offset);
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
            {
                // A rotated hash reports itself here rather than as a failed request.
                _logger.LogWarning("Spotify's playlist API reported: {Message}", errors[0].GetProperty("message").GetString());
                return null;
            }

            if (!document.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("playlistV2", out var playlist) ||
                !playlist.TryGetProperty("content", out var content))
            {
                return null;
            }

            var total = content.TryGetProperty("totalCount", out var totalCount) ? totalCount.GetInt32() : 0;
            var items = new List<SpotifyCollectionItem>();

            if (content.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in array.EnumerateArray())
                {
                    var track = ReadTrack(entry);

                    if (track is not null)
                        items.Add(track);
                }
            }

            return new PlaylistPage(items, total, Rejected: false);
        }

        /// <summary>One page of a playlist, or the news that the session was refused.</summary>
        private record PlaylistPage(IReadOnlyList<SpotifyCollectionItem> Items, int Total, bool Rejected)
        {
            public static readonly PlaylistPage SessionRejected = new([], 0, Rejected: true);
        }

        private static SpotifyCollectionItem? ReadTrack(JsonElement entry)
        {
            if (!entry.TryGetProperty("itemV2", out var itemV2) || !itemV2.TryGetProperty("data", out var data))
                return null;

            var uri = data.TryGetProperty("uri", out var uriValue) ? uriValue.GetString() : null;
            var id = uri?.Split(':').LastOrDefault();

            // Local files and unavailable entries have no track id, so nothing could be fetched
            // for them later.
            if (string.IsNullOrWhiteSpace(id) || uri?.StartsWith("spotify:track:", StringComparison.Ordinal) != true)
                return null;

            var name = data.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;

            var artists = new List<string>();

            if (data.TryGetProperty("artists", out var artistsValue) &&
                artistsValue.TryGetProperty("items", out var artistItems))
            {
                foreach (var artist in artistItems.EnumerateArray())
                {
                    if (artist.TryGetProperty("profile", out var profile) &&
                        profile.TryGetProperty("name", out var artistName))
                    {
                        artists.Add(artistName.GetString() ?? string.Empty);
                    }
                }
            }

            var durationMs = data.TryGetProperty("trackDuration", out var duration) &&
                             duration.TryGetProperty("totalMilliseconds", out var ms)
                ? ms.GetInt64()
                : 0L;

            return new SpotifyCollectionItem(
                name ?? "Unknown",
                string.Join(", ", artists.Where(a => a.Length > 0)),
                durationMs,
                $"https://open.spotify.com/track/{id}");
        }

        /// <summary>
        /// An anonymous access token, kept until shortly before it expires.
        /// </summary>
        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken, bool forceRefresh = false)
        {
            if (!forceRefresh && _cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
                return cached;

            var httpClient = _httpClientFactory.CreateClient(MediaHttpClientDefaults.ClientName);

            var response = await MintTokenAsync(httpClient, refreshSecret: false, cancellationToken);

            // A refused mint usually means Spotify moved to a newer password version while the
            // one in hand still looked fine. Re-reading the secret is the repair; doing it on
            // every mint would hammer the mirror for nothing.
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Spotify refused the token request ({Status}); re-reading the password secret.", (int)response.StatusCode);
                response.Dispose();
                response = await MintTokenAsync(httpClient, refreshSecret: true, cancellationToken);
            }

            using var _ = response;
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var token = document.RootElement.GetProperty("accessToken").GetString()
                ?? throw new InvalidOperationException("Spotify returned no access token.");

            var expiresAt = document.RootElement.TryGetProperty("accessTokenExpirationTimestampMs", out var expiry)
                ? DateTimeOffset.FromUnixTimeMilliseconds(expiry.GetInt64())
                : DateTimeOffset.UtcNow.AddMinutes(30);

            // Held until it is nearly spent - a minute of margin so a request never starts with a
            // token that expires mid-flight. Everything else reuses it; nothing re-mints on a timer.
            _cache.Set(TokenCacheKey, token, expiresAt.AddMinutes(-1));

            return token;
        }

        private async Task<HttpResponseMessage> MintTokenAsync(HttpClient httpClient, bool refreshSecret, CancellationToken cancellationToken)
        {
            var (version, secret) = await GetTotpSecretAsync(httpClient, refreshSecret, cancellationToken);
            var code = GenerateTotp(secret, await GetSpotifyTimeAsync(httpClient, cancellationToken));

            var url = $"https://open.spotify.com/api/token?reason=init&productType=web-player" +
                      $"&totp={code}&totpServer={code}&totpVer={version}";

            return await httpClient.GetAsync(url, cancellationToken);
        }

        /// <summary>
        /// The current one-time-password secret. Rotated by Spotify, so it is read from the
        /// mirror that tracks it rather than written down here.
        /// </summary>
        private async Task<(int Version, IReadOnlyList<int> Secret)> GetTotpSecretAsync(HttpClient httpClient, bool forceRefresh, CancellationToken cancellationToken)
        {
            if (!forceRefresh && _cache.TryGetValue(SecretsCacheKey, out (int, IReadOnlyList<int>) cached))
                return cached;

            using var response = await httpClient.GetAsync(SecretsUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            var newest = document.RootElement.EnumerateObject()
                .Select(property => int.TryParse(property.Name, out var version) ? version : 0)
                .DefaultIfEmpty(0)
                .Max();

            if (newest == 0)
                throw new InvalidOperationException("The secrets mirror returned nothing usable.");

            var secret = document.RootElement.GetProperty(newest.ToString())
                .EnumerateArray()
                .Select(value => value.GetInt32())
                .ToList();

            var result = (newest, (IReadOnlyList<int>)secret);
            _cache.Set(SecretsCacheKey, result, TimeSpan.FromHours(12));

            return result;
        }

        /// <summary>
        /// Spotify's clock. The password is only valid for a thirty second window, so a server
        /// whose own clock has drifted would otherwise mint codes that are refused.
        /// </summary>
        private async Task<long> GetSpotifyTimeAsync(HttpClient httpClient, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await httpClient.GetAsync(ServerTimeUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

                return document.RootElement.GetProperty("serverTime").GetInt64();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not read Spotify's clock; using this machine's.");
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
        }

        /// <summary>
        /// The web player's time-based password: each byte of the secret is mixed with its own
        /// position, and the decimal spelling of the result is the key.
        /// </summary>
        internal static string GenerateTotp(IReadOnlyList<int> secret, long unixSeconds)
        {
            var mixed = string.Concat(secret.Select((value, index) => (value ^ ((index % 33) + 9)).ToString()));
            var key = Encoding.UTF8.GetBytes(mixed);

            var counter = unixSeconds / 30;
            var counterBytes = BitConverter.GetBytes(counter);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(counterBytes);

            using var hmac = new HMACSHA1(key);
            var digest = hmac.ComputeHash(counterBytes);

            var offset = digest[^1] & 0x0F;
            var binary = ((digest[offset] & 0x7F) << 24)
                       | (digest[offset + 1] << 16)
                       | (digest[offset + 2] << 8)
                       | digest[offset + 3];

            return (binary % 1_000_000).ToString("D6");
        }

        /// <summary>
        /// A GET carrying a browser agent, for the pages Spotify lays out differently
        /// depending on who is asking.
        /// </summary>
        private static async Task<string> GetAsBrowserAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        /// <summary>
        /// The hash Spotify will accept for the playlist query, read out of the player's own
        /// bundle. It changes whenever they edit the query, so it is scraped rather than pinned.
        /// </summary>
        private async Task<string> GetFetchPlaylistHashAsync(CancellationToken cancellationToken)
        {
            if (_cache.TryGetValue(HashCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
                return cached;

            var httpClient = _httpClientFactory.CreateClient(MediaHttpClientDefaults.ClientName);

            var home = await GetAsBrowserAsync(httpClient, WebPlayerUrl, cancellationToken);

            var scripts = BundleUrls.Matches(home)
                .Select(match => match.Value)
                .Distinct()
                // The player pack first: it is the one that has carried the query so far, and
                // trying it first usually means downloading exactly one file.
                .OrderByDescending(url => url.Contains("/web-player.", StringComparison.Ordinal))
                .Take(MaxBundlesToSearch)
                .ToList();

            if (scripts.Count == 0)
                throw new InvalidOperationException("The web player page listed no bundles.");

            foreach (var script in scripts)
            {
                string bundle;

                try
                {
                    bundle = await GetAsBrowserAsync(httpClient, script, cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogDebug(ex, "Could not read {Script} while looking for the playlist query.", script);
                    continue;
                }

                foreach (Match match in PersistedQuery.Matches(bundle))
                {
                    if (match.Groups[1].Value != "fetchPlaylist")
                        continue;

                    var hash = match.Groups[2].Value;
                    _cache.Set(HashCacheKey, hash, TimeSpan.FromHours(6));

                    return hash;
                }
            }

            throw new InvalidOperationException("The web player bundle carried no fetchPlaylist query.");
        }
    }
}
