using ArgonFetch.Application.Enums;

namespace ArgonFetch.Application.Dtos
{
    /// <summary>
    /// One downloadable version of a media item.
    /// <para>
    /// Replaces reasoning in terms of "best", "medium" and "worst": those three were whatever
    /// happened to sit at the ends and the middle of the source's format list, so the same label
    /// meant something different from one item to the next. A caller picks from these instead,
    /// with the size on hand to judge how long it will take.
    /// </para>
    /// </summary>
    public class MediaRenditionDto
    {
        /// <summary>Cache key to stream this rendition with.</summary>
        public required string Key { get; set; }

        /// <summary>
        /// Short label for a picker, e.g. "1080p" or "160 kbps". Derived from the resolution or
        /// bitrate rather than the source's own wording, which is not written for readers.
        /// </summary>
        public required string Label { get; set; }

        /// <summary>The source's own description of the format, kept for the curious.</summary>
        public string? Description { get; set; }

        public required string FileExtension { get; set; }

        /// <summary>Media type of the bytes this rendition will deliver.</summary>
        public required string MimeType { get; set; }

        /// <summary>Which stream endpoint serves it - muxing is not the same route as passing bytes through.</summary>
        public UrlType UrlType { get; set; }

        /// <summary>
        /// Transfer size in bytes where the source reports one, so a caller can sort by how fast
        /// a rendition will arrive rather than inferring it from the resolution. Null when the
        /// source does not say, which is the norm for anything that has to be muxed.
        /// </summary>
        public long? FileSizeBytes { get; set; }

        /// <summary>Vertical resolution for video renditions, null for audio.</summary>
        public int? Height { get; set; }

        /// <summary>Bitrate in kbps where known.</summary>
        public double? Bitrate { get; set; }

        /// <summary>
        /// Container the server converts to before sending, or null when the source bytes are
        /// passed through untouched. Clients pass it back as the stream endpoint's format
        /// parameter, so a converted option needs no special case of its own.
        /// </summary>
        public string? ConvertTo { get; set; }
    }
}
