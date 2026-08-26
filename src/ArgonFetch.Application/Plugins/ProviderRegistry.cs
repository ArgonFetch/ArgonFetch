using System.Text.RegularExpressions;
using ArgonFetch.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Application.Plugins
{
    /// <summary>
    /// Decides which provider, if any, wants a link.
    /// </summary>
    public interface IProviderRegistry
    {
        /// <summary>The provider that handles this link, or null to fetch it the ordinary way.</summary>
        ISourceProvider? For(Uri url);

        /// <summary>Every hook, to be applied in turn before a fetch.</summary>
        IReadOnlyList<IFetchOptionsHook> Hooks { get; }

        /// <summary>What is loaded, for the application information endpoint.</summary>
        IReadOnlyList<LoadedPlugin> Plugins { get; }
    }

    public class ProviderRegistry : IProviderRegistry
    {
        /// <summary>
        /// How long one pattern may spend deciding.
        /// <para>
        /// A pattern is written by whoever wrote the plugin, and one that backtracks badly can
        /// take a very long time over a URL built to provoke it. Every request is matched against
        /// every installed pattern, so without a limit here one careless plugin would be enough
        /// to stall the whole application.
        /// </para>
        /// </summary>
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

        private readonly IReadOnlyList<Claim> _providers;
        private readonly ILogger<ProviderRegistry> _logger;

        public ProviderRegistry(IReadOnlyList<LoadedPlugin> plugins, ILogger<ProviderRegistry> logger)
        {
            Plugins = plugins;
            _logger = logger;

            // Configured order, which the loader preserved. Precedence belongs to whoever chose
            // the plugins rather than to a number each plugin declares about itself - given the
            // chance, every author decides theirs is the important one.
            _providers = plugins
                .SelectMany(plugin => plugin.Providers.Select(provider =>
                    new Claim(plugin.Id, provider, Compile(plugin.Id, provider, logger))))
                .ToList();

            Hooks = plugins.SelectMany(plugin => plugin.Hooks).ToList();
        }

        public IReadOnlyList<IFetchOptionsHook> Hooks { get; }

        public IReadOnlyList<LoadedPlugin> Plugins { get; }

        /// <summary>
        /// Compiles a provider's declared patterns once, here, rather than on every request.
        /// </summary>
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
                    // One unusable pattern costs that pattern. The plugin may well have others,
                    // and taking it out entirely over a typo helps nobody.
                    logger.LogWarning(ex, "The {Id} plugin declared a pattern that does not compile: {Pattern}", pluginId, pattern);
                }
            }

            if (compiled.Count == 0)
                logger.LogWarning("The {Id} plugin declares no usable link patterns, so it will never be asked", pluginId);

            return compiled;
        }

        public ISourceProvider? For(Uri url)
        {
            // Every provider is asked, rather than stopping at the first that says yes. A second
            // claim on the same link is worth knowing about: left unsaid, the losing plugin
            // simply never runs and nobody can see why.
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
                // The declared patterns first, then the provider's own say - which almost every
                // provider leaves at the default, because the patterns were the whole answer.
                return entry.Patterns.Any(pattern => pattern.IsMatch(address)) && entry.Provider.CanHandle(url);
            }
            catch (RegexMatchTimeoutException)
            {
                LogPatternTimeout(entry.PluginId, address);
                return false;
            }
            catch (Exception ex)
            {
                // A provider that throws while deciding has answered no. It is asked on every
                // request, including ones for sources it has nothing to do with, so a fault
                // here must not be able to break an unrelated download.
                _logger.LogWarning(ex, "The {Id} plugin threw while deciding about {Url}", entry.PluginId, url);
                return false;
            }
        }

        private void LogPatternTimeout(string pluginId, string address) =>
            _logger.LogWarning("A link pattern from the {Id} plugin took too long over {Url} and was abandoned", pluginId, address);

        private sealed record Claim(string PluginId, ISourceProvider Provider, IReadOnlyList<Regex> Patterns);
    }
}
