namespace ArgonFetch.Application.Plugins
{
    // Desired state: what is listed is installed, what is not is removed.
    public class PluginOptions
    {
        public const string SectionName = "Plugins";

        public List<string> Repositories { get; set; } = [];

        // "id", or "id@1.2.0" to pin. Order is precedence when two plugins claim one link.
        public List<string> Install { get; set; } = [];

        public string Path { get; set; } = "plugins";

        public Dictionary<string, Dictionary<string, string?>> Settings { get; set; } = [];
    }

    public sealed record PluginIndexEntry
    {
        public string Id { get; init; } = string.Empty;
        public string? Name { get; init; }
        public string Version { get; init; } = string.Empty;

        public int Abi { get; init; }

        public string File { get; init; } = string.Empty;

        public string Sha256 { get; init; } = string.Empty;
    }
}
