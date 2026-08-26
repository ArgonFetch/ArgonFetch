using System.Text.RegularExpressions;
using ArgonFetch.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Application.Plugins
{
    public interface IProviderRegistry
    {
        ISourceProvider? For(Uri url);

        IReadOnlyList<IFetchOptionsHook> Hooks { get; }

        IReadOnlyList<LoadedPlugin> Plugins { get; }
    }

    public class ProviderRegistry : IProviderRegistry
    {
        // A plugin's pattern can backtrack badly enough to stall every request.
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

        private readonly IReadOnlyList<Claim> _providers;
        private readonly ILogger<ProviderRegistry> _logger;

        public ProviderRegistry(IReadOnlyList<LoadedPlugin> plugins, ILogger<ProviderRegistry> logger)
        {
            Plugins = plugins;
            _logger = logger;

            // Configured order is precedence, so the choice stays with whoever installed them.
            _providers = plugins
                .SelectMany(plugin => plugin.Providers.Select(provider =>
                    new Claim(plugin.Id, provider, Compile(plugin.Id, provider, logger))))
                .ToList();

            Hooks = plugins.SelectMany(plugin => plugin.Hooks).ToList();
        }

        public IReadOnlyList<IFetchOptionsHook> Hooks { get; }

        public IReadOnlyList<LoadedPlugin> Plugins { get; }

        private static IReadOnlyList<Regex> Compile(string pluginId, ISourceProvider provider, ILogger logger)
        {
            var compiled = new List<Regex>();

            IReadOnlyList<string> patterns;

            try
            {
                patterns = provider.UrlPatterns ?? [];
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The {Id} plugin threw while listing the links it wants", pluginId);
                return compiled;
            }

            foreach (var pattern in patterns)
            {
                try
                {
                    compiled.Add(new Regex(
                        pattern,
                        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
                        MatchTimeout));
                }
                catch (ArgumentException ex)
                {
                    logger.LogWarning(ex, "The {Id} plugin declared a pattern that does not compile: {Pattern}", pluginId, pattern);
                }
            }

            if (compiled.Count == 0)
                logger.LogWarning("The {Id} plugin declares no usable link patterns, so it will never be asked", pluginId);

            return compiled;
        }

        public ISourceProvider? For(Uri url)
        {
            var address = url.ToString();
            var claimed = _providers.Where(entry => Claims(entry, url, address)).ToList();

            if (claimed.Count == 0)
                return null;

            if (claimed.Count > 1)
            {
                _logger.LogWarning(
                    "{Winner} handles {Url}; also claimed by {Others}",
                    claimed[0].PluginId, url,
                    string.Join(", ", claimed.Skip(1).Select(entry => entry.PluginId)));
            }

            return claimed[0].Provider;
        }

        private bool Claims(Claim entry, Uri url, string address)
        {
            try
            {
                return entry.Patterns.Any(pattern => pattern.IsMatch(address)) && entry.Provider.CanHandle(url);
            }
            catch (RegexMatchTimeoutException)
            {
                LogPatternTimeout(entry.PluginId, address);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "The {Id} plugin threw while deciding about {Url}", entry.PluginId, url);
                return false;
            }
        }

        private void LogPatternTimeout(string pluginId, string address) =>
            _logger.LogWarning("A link pattern from the {Id} plugin took too long over {Url} and was abandoned", pluginId, address);

        private sealed record Claim(string PluginId, ISourceProvider Provider, IReadOnlyList<Regex> Patterns);
    }
}
