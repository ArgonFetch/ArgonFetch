namespace ArgonFetch.Abstractions
{
    public sealed record MediaTags(string? Title, string? Artist);

    public sealed record CollectionResult(
        string? Title,
        string? Author,
        string? CoverUrl,
        IReadOnlyList<CollectionEntry> Items)
    {
        public bool MayBeTruncated { get; init; }
    }

    public sealed record CollectionEntry(Uri Url, string Title, string? Author = null, string? CoverUrl = null);

    public sealed record MediaResult(
        string Title,
        string? Author,
        string? CoverUrl,
        IReadOnlyList<MediaStream> Streams);

    public sealed record MediaStream(Uri Url, bool IsAudio)
    {
        public string? Label { get; init; }

        public string? MimeType { get; init; }

        public string? FileExtension { get; init; }

        public long? SizeBytes { get; init; }

        public string? Proxy { get; init; }
    }

    public sealed record ProbeResult(string? Title, string? Uploader, double? DurationSeconds);
}
