namespace ArgonFetch.Application.Plugins
{
    /// <summary>
    /// Which plugins an operator wants, and where to get them.
    /// <para>
    /// Read as desired state rather than as instructions: what is listed is installed, what is
    /// not is removed. A list that says what should be true is one you can read and know what
    /// the machine is running; a list of things that were once installed is not.
    /// </para>
    /// </summary>
    public class PluginOptions
    {
        public const string SectionName = "Plugins";

        public List<string> Repositories { get; set; } = [];

        /// <summary>
        /// Plugins to have installed, as "id" for the newest compatible build or "id@1.2.0" to
        /// pin one.
        /// <para>
        /// The order is also precedence: where two plugins both claim a link, the one listed
        /// first wins. Settling it here rather than by a number each plugin declares for itself
        /// keeps the decision with the person who chose the plugins - and avoids every author
        /// deciding theirs is the important one.
        /// </para>
        /// </summary>
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
