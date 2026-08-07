namespace ArgonFetch.Application.Services
{
    /// <summary>
    /// Shared configuration for the HTTP client used to fetch media from upstream CDNs.
    /// The User-Agent is what keeps YouTube from returning 403, so it lives in one place
    /// rather than being repeated at every call site.
    /// </summary>
    public static class MediaHttpClientDefaults
    {
        /// <summary>
        /// Name of the client registered with IHttpClientFactory.
        /// </summary>
        public const string ClientName = "media";

        public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
    }
}
