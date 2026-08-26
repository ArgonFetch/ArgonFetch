using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Abstractions
{
    public interface IProviderContext
    {
        // rotateProxy: leave off for a source that signs an address for whoever asked.
        HttpClient CreateHttpClient(bool rotateProxy = true);

        IMemoryCache Cache { get; }

        ILogger Logger { get; }

        IReadOnlyDictionary<string, string?> Settings { get; }

        Task<ProbeResult?> ProbeAsync(Uri url, CancellationToken cancellationToken);

        string CacheKey(string key);
    }
}
