namespace ArgonFetch.Abstractions
{
    /// <summary>
    /// Marks an assembly as an ArgonFetch plugin and states which contract it was built against.
    /// <para>
    /// Read before anything in the assembly is instantiated, so a plugin written for a contract
    /// this host does not implement is set aside with a line in the log rather than loaded and
    /// discovered halfway through a request.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class ArgonFetchPluginAttribute : Attribute
    {
        public ArgonFetchPluginAttribute(string id, int abi)
        {
            Id = id;
            Abi = abi;
        }

        /// <summary>The id this plugin is installed and configured under.</summary>
        public string Id { get; }

        /// <summary>
        /// Major version of ArgonFetch.Abstractions this was built against. The host implements
        /// exactly one, and refuses anything else - a contract that changed shape is not
        /// something to guess at.
        /// </summary>
        public int Abi { get; }

        /// <summary>Shown to an operator choosing what to install.</summary>
        public string? Name { get; init; }

        /// <summary>The current contract, for a plugin that does not want to spell out a number.</summary>
        public const int CurrentAbi = 1;
    }
}
