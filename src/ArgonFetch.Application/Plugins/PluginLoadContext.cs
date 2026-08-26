using System.Reflection;
using System.Runtime.Loader;

namespace ArgonFetch.Application.Plugins
{
    // Each plugin loads privately so its dependency versions stay its own problem. The
    // contract is the exception: a plugin using its own copy gets a different type, and the
    // cast fails claiming ISourceProvider cannot be cast to ISourceProvider.
    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        private static readonly string[] Shared =
        [
            "ArgonFetch.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.Caching.Abstractions",
            "Microsoft.Extensions.Primitives",
        ];

        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath)
            : base(name: Path.GetFileNameWithoutExtension(pluginPath), isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Null defers to the default context, where the host keeps its copy.
            if (Shared.Contains(assemblyName.Name, StringComparer.OrdinalIgnoreCase))
                return null;

            var path = _resolver.ResolveAssemblyToPath(assemblyName);

            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
