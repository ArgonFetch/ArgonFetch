namespace ArgonFetch.Application.Dtos
{
    /// <summary>
    /// One playable stream. <see cref="Path"/> is relative on purpose: the caller knows which
    /// origin it reached, and an absolute URL built from the request would hand an https page an
    /// http link behind a reverse proxy.
    /// </summary>
    public class PlaybackTrackDto
    {
        public required string Key { get; set; }

        /// <summary>Path under the instance that streams this track, ranges included.</summary>
        public required string Path { get; set; }

        public required string Label { get; set; }

        public string? Description { get; set; }

        /// <summary>
        /// Full type with codecs, as <c>canPlayType</c> and <c>MediaSource.isTypeSupported</c>
        /// want it - <c>video/mp4; codecs="avc1.640028"</c>. Without the codecs a browser can only
        /// guess, and guesses wrong on WebM.
        /// </summary>
        public required string ContentType { get; set; }

        public required string MimeType { get; set; }

        public string? Codec { get; set; }

        public int? Width { get; set; }

        public int? Height { get; set; }

        public double? Fps { get; set; }

        public double? Bitrate { get; set; }

        public long? FileSizeBytes { get; set; }
    }
}
