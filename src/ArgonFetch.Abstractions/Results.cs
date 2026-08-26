namespace ArgonFetch.Abstractions
{
    /// <summary>
    /// What a downloaded file should say it is. Carried from whatever identified the media to
    /// whatever serves it.
    /// </summary>
    public sealed record MediaTags(string? Title, string? Artist);

    /// <summary>
    /// A collection, as a list of links and what little is known about each.
    /// </summary>
    public sealed record CollectionResult(
        string? Title,
        string? Author,
        string? CoverUrl,
        IReadOnlyList<CollectionEntry> Items)
    {
        /// <summary>
        /// Whether the source is known to have sent only part of the list. Reported rather than
        /// hidden: a listing that quietly stops at a hundred looks exactly like a hundred-entry
        /// collection, and nobody notices what is missing.
        /// </summary>
        public bool MayBeTruncated { get; init; }
    }

    /// <summary>
    /// One entry of a collection. Deliberately not resolved - the link is enough, and following
    /// it is what happens when somebody picks this entry.
    /// </summary>
    public sealed record CollectionEntry(Uri Url, string Title, string? Author = null, string? CoverUrl = null);

    /// <summary>
    /// Media a provider fetched by itself.
    /// </summary>
    public sealed record MediaResult(
        string Title,
        string? Author,
        string? CoverUrl,
        IReadOnlyList<MediaStream> Streams);

    /// <summary>
    /// One downloadable stream, as the source serves it.
    /// <para>
    /// A plain address and what is known about it, nothing more. Caching it, hiding it behind a
    /// key and building the URL a client is given are all the host's business - a plugin that
    /// did any of that would be coupled to how the host serves bytes, which changes.
    /// </para>
    /// </summary>
    public sealed record MediaStream(Uri Url, bool IsAudio)
    {
        /// <summary>Shown to whoever is choosing, e.g. "1080p" or "128 kbps".</summary>
        public string? Label { get; init; }

        /// <summary>Media type of the bytes at <see cref="Url"/>, where the source declares one.</summary>
        public string? MimeType { get; init; }

        /// <summary>Extension the file should be saved as, including the dot.</summary>
        public string? FileExtension { get; init; }

        /// <summary>Transfer size where the source reports one; it is what tells a reader how long this will take.</summary>
        public long? SizeBytes { get; init; }

        /// <summary>
        /// Proxy this address was obtained through, when it was. Sources commonly sign an address
        /// for the address that asked for it, so fetching it from anywhere else is refused.
        /// </summary>
        public string? Proxy { get; init; }
    }

    /// <summary>
    /// The little that can be learned about a link without downloading it. Enough to tell one
    /// candidate recording from another, which is what it is for.
    /// </summary>
    public sealed record ProbeResult(string? Title, string? Uploader, double? DurationSeconds);
}
