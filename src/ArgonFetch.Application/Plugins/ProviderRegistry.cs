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
        private readonly IReadOnlyList<(string PluginId, ISourceProvider Provider)> _providers;
        private readonly ILogger<ProviderRegistry> _logger;

        public ProviderRegistry(IReadOnlyList<LoadedPlugin> plugins, ILogger<ProviderRegistry> logger)
        {
            Plugins = plugins;
            _logger = logger;

            // Configured order, which the loader preserved. Precedence belongs to whoever chose
            // the plugins rather than to a number each plugin declares about itself - given the
            // chance, every author decides theirs is the important one.
            _providers = plugins
                .SelectMany(plugin => plugin.Providers.Select(provider => (plugin.Id, provider)))
                .ToList();

            Hooks = plugins.SelectMany(plugin => plugin.Hooks).ToList();
        }

        public IReadOnlyList<IFetchOptionsHook> Hooks { get; }

        public IReadOnlyList<LoadedPlugin> Plugins { get; }

        public ISourceProvider? For(Uri url)
        {
            // Every provider is asked, rather than stopping at the first that says yes. A second
            // claim on the same link is worth knowing about: left unsaid, the losing plugin
            // simply never runs and nobody can see why.
            var claimed = _providers.Where(entry => Claims(entry, url)).ToList();

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

        private bool Claims((string PluginId, ISourceProvider Provider) entry, Uri url)
        {
            try
            {
                return entry.Provider.CanHandle(url);
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
    }
}
