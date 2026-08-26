using ArgonFetch.Abstractions;
using ArgonFetch.Application.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Application.Plugins
{
    /// <summary>Makes a context for one plugin to work with.</summary>
    public interface IProviderContextFactory
    {
        IProviderContext For(string pluginId, Func<Uri, CancellationToken, Task<ProbeResult?>> probe);
    }

    public class ProviderContextFactory : IProviderContextFactory
    {
        private readonly IMediaHttpClients _httpClients;
        private readonly IProxyPool _proxyPool;
        private readonly IMemoryCache _cache;
        private readonly ILoggerFactory _loggerFactory;
        private readonly PluginOptions _options;

        public ProviderContextFactory(
            IMediaHttpClients httpClients,
            IProxyPool proxyPool,
            IMemoryCache cache,
            ILoggerFactory loggerFactory,
            PluginOptions options)
        {
            _httpClients = httpClients;
            _proxyPool = proxyPool;
            _cache = cache;
            _loggerFactory = loggerFactory;
            _options = options;
        }

        public IProviderContext For(string pluginId, Func<Uri, CancellationToken, Task<ProbeResult?>> probe) =>
            new ProviderContext(
                pluginId,
                _httpClients,
                _proxyPool,
                _cache,
                _loggerFactory.CreateLogger($"Plugin.{pluginId}"),
                _options.Settings.TryGetValue(pluginId, out var settings) ? settings : [],
                probe);

        private sealed class ProviderContext : IProviderContext
        {
            private readonly string _pluginId;
            private readonly IMediaHttpClients _httpClients;
            private readonly IProxyPool _proxyPool;
            private readonly Func<Uri, CancellationToken, Task<ProbeResult?>> _probe;

            public ProviderContext(
                string pluginId,
                IMediaHttpClients httpClients,
                IProxyPool proxyPool,
                IMemoryCache cache,
                ILogger logger,
                IReadOnlyDictionary<string, string?> settings,
                Func<Uri, CancellationToken, Task<ProbeResult?>> probe)
            {
                _pluginId = pluginId;
                _httpClients = httpClients;
                _proxyPool = proxyPool;
                _probe = probe;
                Cache = cache;
                Logger = logger;
                Settings = settings;
            }

            public IMemoryCache Cache { get; }

            public ILogger Logger { get; }

            public IReadOnlyDictionary<string, string?> Settings { get; }

            // Whichever client the pool's next proxy needs, or the plain one. The same service
            // the rest of the application fetches media through, so a plugin is subject to the
            // same rotation rather than quietly going direct.
            public HttpClient CreateHttpClient(bool rotateProxy = true) =>
                _httpClients.For(rotateProxy ? _proxyPool.Next() : null);

            public Task<ProbeResult?> ProbeAsync(Uri url, CancellationToken cancellationToken) =>
                _probe(url, cancellationToken);

            // Prefixed so two plugins caching "track:1" cannot overwrite one another - the cache
            // is the application's, not any one plugin's.
            public string CacheKey(string key) => $"plugin:{_pluginId}:{key}";
        }
    }
}
