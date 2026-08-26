using ArgonFetch.Abstractions;
using ArgonFetch.Application.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Application.Plugins
{
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

            public HttpClient CreateHttpClient(bool rotateProxy = true) =>
                _httpClients.For(rotateProxy ? _proxyPool.Next() : null);

            public Task<ProbeResult?> ProbeAsync(Uri url, CancellationToken cancellationToken) =>
                _probe(url, cancellationToken);

            public string CacheKey(string key) => $"plugin:{_pluginId}:{key}";
        }
    }
}
