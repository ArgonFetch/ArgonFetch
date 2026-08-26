using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Abstractions
{
    /// <summary>
    /// What the host lends a provider.
    /// <para>
    /// Capabilities, never the container. Handing over a service provider would make every type
    /// the host happens to register part of this contract, and the first plugin to reach for one
    /// would freeze it there.
    /// </para>
    /// </summary>
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

        /// <summary>
        /// Shared with the rest of the application, so a plugin must key entries with something
        /// of its own. <see cref="CacheKey"/> does that.
        /// </summary>
        IMemoryCache Cache { get; }

        /// <summary>Named for the plugin, so its lines are attributable in a shared log.</summary>
        ILogger Logger { get; }

        /// <summary>
        /// Settings the operator wrote for this plugin, from the configuration section named
        /// after it. Empty when none were given - a plugin that needs one has to say so itself.
        /// </summary>
        IReadOnlyDictionary<string, string?> Settings { get; }

        /// <summary>
        /// What the fetch engine can say about a link without downloading it.
        /// <para>
        /// For choosing between candidates: a provider that has found several possible matches
        /// for a recording can ask how long each one runs and keep the one that fits. Null when
        /// the link cannot be read at all, which is an answer rather than a fault.
        /// </para>
        /// <para>
        /// The result is remembered for the length of the request, so probing a link and then
        /// handing that same link back as a rewrite does not fetch it twice.
        /// </para>
        /// </summary>
        Task<ProbeResult?> ProbeAsync(Uri url, CancellationToken cancellationToken);

        /// <summary>
        /// A cache key belonging to this plugin, so two plugins caching "track:1" do not
        /// overwrite each other.
        /// </summary>
        string CacheKey(string key);
    }
}
