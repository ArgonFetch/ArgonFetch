using System.Reflection;
using System.Runtime.Loader;
using ArgonFetch.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Application.Plugins
{
    public sealed record LoadedPlugin(
        string Id,
        string? Name,
        string Version,
        IReadOnlyList<ISourceProvider> Providers,
        IReadOnlyList<IFetchOptionsHook> Hooks)
    {
        /// <summary>
        /// The context this plugin's assemblies live in, held only so that it keeps living.
        /// <para>
        /// A collectible context is collected once nothing refers to it, and unloading takes the
        /// plugin's own dependencies with it - which does not show up until the plugin first
        /// reaches for one and is told the context is already unloaded. The providers above are
        /// not enough to keep it: they are instances, and an instance does not root the context
        /// its type was loaded into.
        /// </para>
        /// </summary>
        internal AssemblyLoadContext? Context { get; init; }
    }

    public class PluginLoader
    {
        private readonly ILogger<PluginLoader> _logger;

        public PluginLoader(ILogger<PluginLoader> logger) => _logger = logger;

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
                    _logger.LogError(ex, "Could not load the {Id} plugin", parsed.Value.Id);
                }
            }

            return loaded;
        }

        private LoadedPlugin? LoadOne(string folder, string id)
        {
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

                return new LoadedPlugin(manifest.Id, manifest.Name, version, providers, hooks) { Context = context };
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
