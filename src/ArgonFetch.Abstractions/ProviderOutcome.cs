namespace ArgonFetch.Abstractions
{
    /// <summary>
    /// What a provider decided to do with a link. There are four answers and no others.
    /// </summary>
    public abstract record ProviderOutcome
    {
        private ProviderOutcome() { }

        /// <summary>
        /// Nothing to do - let yt-dlp fetch the original link. Also the right answer when a
        /// provider recognises the link but finds nothing special about this particular one.
        /// </summary>
        public static ProviderOutcome PassThrough { get; } = new PassThroughOutcome();

        /// <summary>
        /// Fetch a different link instead, and describe the result with these tags.
        /// <para>
        /// Spotify serves no audio anyone can download, so its provider finds the same recording
        /// elsewhere and points the fetch at that - while keeping Spotify's own title and credit,
        /// which are the reason the track was matched rather than merely searched for.
        /// </para>
        /// </summary>
        public static ProviderOutcome Rewrite(Uri url, MediaTags tags, string? coverUrl = null) =>
            new RewriteOutcome(url, tags, coverUrl);

        /// <summary>
        /// The link names a collection. Entries are listed, not resolved - each one is fetched
        /// through the ordinary path when somebody actually asks for it, because resolving a
        /// thousand of them to show a list would take the better part of an hour.
        /// </summary>
        public static ProviderOutcome Listing(CollectionResult collection) =>
            new ListingOutcome(collection);

        /// <summary>
        /// The provider fetched it. yt-dlp is not run at all.
        /// </summary>
        public static ProviderOutcome Complete(MediaResult media) =>
            new CompleteOutcome(media);

        /// <summary>Not this provider's link after all - try the next one, then yt-dlp.</summary>
        public static ProviderOutcome Declined { get; } = new DeclinedOutcome();

        public sealed record PassThroughOutcome : ProviderOutcome;
        public sealed record DeclinedOutcome : ProviderOutcome;
        public sealed record RewriteOutcome(Uri Url, MediaTags Tags, string? CoverUrl) : ProviderOutcome;
        public sealed record ListingOutcome(CollectionResult Collection) : ProviderOutcome;
        public sealed record CompleteOutcome(MediaResult Media) : ProviderOutcome;
    }
}
