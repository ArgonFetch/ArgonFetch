namespace ArgonFetch.Application.Services
{
    public interface IProxyPool
    {
        int Count { get; }

        string? Next();
    }

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

            var index = (uint)Interlocked.Increment(ref _cursor) % (uint)_proxies.Length;

            return _proxies[index];
        }

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
