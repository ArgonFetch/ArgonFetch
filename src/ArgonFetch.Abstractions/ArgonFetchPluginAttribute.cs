namespace ArgonFetch.Abstractions
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class ArgonFetchPluginAttribute : Attribute
    {
        public ArgonFetchPluginAttribute(string id, int abi)
        {
            Id = id;
            Abi = abi;
        }

        public string Id { get; }

        public int Abi { get; }

        public string? Name { get; init; }

        public const int CurrentAbi = 1;
    }
}
