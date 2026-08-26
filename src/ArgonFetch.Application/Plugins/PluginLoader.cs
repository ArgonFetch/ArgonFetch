using System.Reflection;
using ArgonFetch.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Application.Plugins
{
    /// <summary>A plugin that was loaded, and what came out of it.</summary>
    public sealed record LoadedPlugin(
        string Id,
        string? Name,
        string Version,
        IReadOnlyList<ISourceProvider> Providers,
        IReadOnlyList<IFetchOptionsHook> Hooks);

    /// <summary>
    /// Turns the plugins folder into objects the application can call.
    /// </summary>
    public class PluginLoader
    {
        private readonly ILogger<PluginLoader> _logger;

        public PluginLoader(ILogger<PluginLoader> logger) => _logger = logger;

        /// <summary>
        /// Loads the requested plugins, in the order they were requested - which is the order
        /// their providers are then asked in.
        /// </summary>
        public IReadOnlyList<LoadedPlugin> Load(string root, IEnumerable<string> install)
        {
            var loaded = new List<LoadedPlugin>();

            if (!Directory.Exists(root))
                return loaded;

            foreach (var request in install)
            {
                var parsed = PluginInstaller.Parse(request);

                if (parsed is null)
                    continue;

                var folder = Path.Combine(root, parsed.Value.Id);

                if (!Directory.Exists(folder))
                {
                    _logger.LogWarning("The {Id} plugin is configured but not installed", parsed.Value.Id);
                    continue;
                }

                try
                {
                    var plugin = LoadOne(folder, parsed.Value.Id);

                    if (plugin is not null)
                        loaded.Add(plugin);
                }
                catch (Exception ex)
                {
                    // One bad plugin costs its own sources and nothing else.
                    _logger.LogError(ex, "Could not load the {Id} plugin", parsed.Value.Id);
                }
            }

            return loaded;
        }

        private LoadedPlugin? LoadOne(string folder, string id)
        {
            // The assembly named after the folder, or failing that whichever one declares
            // itself a plugin. Named first because it is the cheap answer and almost always
            // the right one.
            var candidates = Directory.GetFiles(folder, "*.dll")
                .OrderByDescending(path => Path.GetFileNameWithoutExtension(path)
                    .EndsWith(id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var path in candidates)
            {
                var context = new PluginLoadContext(path);
                Assembly assembly;

                try
                {
                    assembly = context.LoadFromAssemblyPath(path);
                }
                catch (BadImageFormatException)
                {
                    // A native library sitting beside the managed ones.
                    context.Unload();
                    continue;
                }

                var manifest = assembly.GetCustomAttribute<ArgonFetchPluginAttribute>();

                if (manifest is null)
                {
                    context.Unload();
                    continue;
                }

                if (manifest.Abi != ArgonFetchPluginAttribute.CurrentAbi)
                {
                    // Set aside before anything in it runs. A contract that changed shape is not
                    // something to find out about halfway through somebody's download.
                    _logger.LogWarning(
                        "The {Id} plugin was built for contract {Theirs}; this build implements {Ours}",
                        manifest.Id, manifest.Abi, ArgonFetchPluginAttribute.CurrentAbi);

                    context.Unload();
                    return null;
                }

                var providers = Instantiate<ISourceProvider>(assembly);
                var hooks = Instantiate<IFetchOptionsHook>(assembly);

                if (providers.Count == 0 && hooks.Count == 0)
                {
                    _logger.LogWarning("The {Id} plugin declares itself one but offers nothing", manifest.Id);
                    context.Unload();
                    return null;
                }

                var version = ReadVersion(folder);

                _logger.LogInformation(
                    "Loaded the {Id} plugin {Version} ({Providers} provider(s), {Hooks} hook(s))",
                    manifest.Id, version, providers.Count, hooks.Count);

                return new LoadedPlugin(manifest.Id, manifest.Name, version, providers, hooks);
            }

            _logger.LogWarning("Nothing in {Folder} declares itself an ArgonFetch plugin", folder);
            return null;
        }

        private List<T> Instantiate<T>(Assembly assembly) where T : class
        {
            var made = new List<T>();

            foreach (var type in assembly.GetExportedTypes())
            {
                if (type.IsAbstract || type.IsInterface || !typeof(T).IsAssignableFrom(type))
                    continue;

                // Parameterless only, as the manifest is read before any of the host's services
                // could be handed over. What a plugin needs arrives with each call instead.
                if (type.GetConstructor(Type.EmptyTypes) is null)
                {
                    _logger.LogWarning("{Type} has no parameterless constructor and was skipped", type.FullName);
                    continue;
                }

                if (Activator.CreateInstance(type) is T instance)
                    made.Add(instance);
            }

            return made;
        }

        private static string ReadVersion(string folder)
        {
            var marker = Path.Combine(folder, ".version");

            return File.Exists(marker) ? File.ReadAllText(marker).Trim() : "unknown";
        }
    }
}
