using SpotifyAPI.Web;

namespace ArgonFetch.Application.Services
{
    /// <summary>
    /// Holds the Spotify client, which is only available when credentials are configured.
    /// Spotify support is optional, so this wrapper is always resolvable from DI and
    /// callers check <see cref="IsConfigured"/> - registering a null SpotifyClient
    /// directly would inject null into every consumer instead.
    /// </summary>
    public sealed class SpotifyClientProvider
    {
        public SpotifyClientProvider(SpotifyClient? client)
        {
            Client = client;
        }

        public SpotifyClient? Client { get; }

        public bool IsConfigured => Client is not null;

        /// <summary>
        /// Returns the configured client, or throws a descriptive error when Spotify
        /// credentials are missing.
        /// </summary>
        public SpotifyClient Require()
        {
            return Client ?? throw new NotSupportedException(
                "Spotify support is not configured on this instance. " +
                "Set the Spotify:ClientId and Spotify:ClientSecret configuration values to enable it.");
        }
    }
}
