namespace ArgonFetch.Application.Services
{
    public interface IProxyPool
    {
        /// <summary>Number of proxies loaded; 0 means fetches go out from the server's own IP.</summary>
        int Count { get; }

        /// <summary>Next proxy in round-robin order, or null when no list is configured.</summary>
        string? Next();
    }

    /// <summary>
    /// Round-robins the proxies listed in the file at <c>PROXY_LIST_PATH</c> (one per line,
    /// blank lines and # comments ignored) so repeated yt-dlp fetches do not all leave from
    /// the same IP and trip a block.
    /// </summary>
    public class ProxyPool : IProxyPool
    {
        private readonly string[] _proxies;
        private int _cursor = -1;

        public ProxyPool(string[] proxies) => _proxies = proxies;

        public int Count => _proxies.Length;

        public string? Next()
        {
            if (_proxies.Length == 0)
                return null;

            // Unsigned so the index stays valid once the counter wraps past int.MaxValue.
            var index = (uint)Interlocked.Increment(ref _cursor) % (uint)_proxies.Length;

            return _proxies[index];
        }

        /// <summary>
        /// Reads the proxy list. A missing or unreadable file is not fatal: the pool is simply
        /// empty and fetches behave as they did before a list was configured.
        /// </summary>
        public static string[] ReadList(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return [];

            return File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .Select(Normalize)
                .ToArray();
        }

        /// <summary>
        /// Turns a plain <c>host:port</c> or a provider export like Webshare's
        /// <c>host:port:user:pass</c> into the URL yt-dlp expects. Lines that already carry a
        /// scheme are passed through untouched.
        /// </summary>
        private static string Normalize(string line)
        {
            if (line.Contains("://"))
                return line;

            var parts = line.Split(':');

            return parts.Length switch
            {
                2 => $"http://{parts[0]}:{parts[1]}",
                4 => $"http://{parts[2]}:{parts[3]}@{parts[0]}:{parts[1]}",
                _ => line,
            };
        }
    }
}
