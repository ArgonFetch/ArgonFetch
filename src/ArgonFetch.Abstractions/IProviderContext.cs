using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Abstractions
{
    public interface IProviderContext
    {
        /// <summary>
        /// A client for talking to the source.
        /// </summary>
        /// <param name="rotateProxy">
        /// Whether to go through the next proxy in the pool. Sources that count requests per
        /// address want this; one that hands out an address signed for the caller does not,
        /// because the download that follows has to come from the same place.
        /// </param>
        HttpClient CreateHttpClient(bool rotateProxy = true);

        IMemoryCache Cache { get; }

        ILogger Logger { get; }

        IReadOnlyDictionary<string, string?> Settings { get; }

        Task<ProbeResult?> ProbeAsync(Uri url, CancellationToken cancellationToken);

        string CacheKey(string key);
    }
}
