using System.Reflection;
using System.Runtime.Loader;

namespace ArgonFetch.Application.Plugins
{
    /// <summary>
    /// One plugin's own corner of the process.
    /// <para>
    /// A plugin brings its own dependencies, and two plugins will eventually bring different
    /// versions of the same one. Each gets its own context so that stays their problem rather
    /// than becoming a version conflict nobody can resolve.
    /// </para>
    /// <para>
    /// The contract is the exception, and it has to be. Types have identity per context, so a
    /// plugin loading its own copy of ArgonFetch.Abstractions would implement an
    /// <c>ISourceProvider</c> unrelated to the one the host is looking for - and say so with a
    /// message claiming ISourceProvider cannot be cast to ISourceProvider. It is deliberately
    /// resolved from the host instead, which is why a plugin references the package without
    /// shipping it.
    /// </para>
    /// </summary>
    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        private static readonly string[] Shared =
        [
            "ArgonFetch.Abstractions",
            // The two the contract itself exposes. A plugin handed an ILogger from its own copy
            // of the abstractions could not be given the host's logger.
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.Caching.Abstractions",
            "Microsoft.Extensions.Primitives",
        ];

        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath)
            // Collectible so a plugin can be unloaded; without it, replacing one means a restart.
            : base(name: Path.GetFileNameWithoutExtension(pluginPath), isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Null hands the request back to the default context, which is where the host's own
            // copy lives. Anything else the plugin brought is loaded privately below.
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
