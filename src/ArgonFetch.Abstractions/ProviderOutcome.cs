namespace ArgonFetch.Abstractions
{
    public abstract record ProviderOutcome
    {
        private ProviderOutcome() { }

        public static ProviderOutcome PassThrough { get; } = new PassThroughOutcome();

        public static ProviderOutcome Rewrite(Uri url, MediaTags tags, string? coverUrl = null) =>
            new RewriteOutcome(url, tags, coverUrl);

        public static ProviderOutcome Listing(CollectionResult collection) =>
            new ListingOutcome(collection);

        public static ProviderOutcome Complete(MediaResult media) =>
            new CompleteOutcome(media);

        public static ProviderOutcome Declined { get; } = new DeclinedOutcome();

        public sealed record PassThroughOutcome : ProviderOutcome;
        public sealed record DeclinedOutcome : ProviderOutcome;
        public sealed record RewriteOutcome(Uri Url, MediaTags Tags, string? CoverUrl) : ProviderOutcome;
        public sealed record ListingOutcome(CollectionResult Collection) : ProviderOutcome;
        public sealed record CompleteOutcome(MediaResult Media) : ProviderOutcome;
    }
}
