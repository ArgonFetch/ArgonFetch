namespace ArgonFetch.Application.Services
{
    /// <summary>
    /// Maps a source container to the media type it is served with. Clients pick the on-disk
    /// name and the tagging path from the mime type, so it has to describe the bytes that are
    /// actually sent rather than the format the API would like them to be in.
    /// </summary>
    public static class MediaFormats
    {
        private static readonly Dictionary<string, string> MimeTypesByExtension =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".mp3"] = "audio/mpeg",
                [".m4a"] = "audio/mp4",
                [".aac"] = "audio/aac",
                [".opus"] = "audio/opus",
                [".ogg"] = "audio/ogg",
                [".flac"] = "audio/flac",
                [".wav"] = "audio/wav",
                [".mp4"] = "video/mp4",
                [".m4v"] = "video/mp4",
                [".mkv"] = "video/x-matroska",
                [".mov"] = "video/quicktime",
            };

        /// <summary>
        /// The media type for a file extension, or null when the container is unknown and the
        /// caller therefore has to convert rather than pass the bytes through.
        /// <para>
        /// WebM carries either audio or video, so the caller says which one it asked for.
        /// </para>
        /// </summary>
        public static string? MimeTypeFor(string? fileExtension, bool isAudio)
        {
            if (string.IsNullOrWhiteSpace(fileExtension))
                return null;

            var extension = fileExtension.Trim();

            if (!extension.StartsWith('.'))
                extension = "." + extension;

            if (extension.Equals(".webm", StringComparison.OrdinalIgnoreCase))
                return isAudio ? "audio/webm" : "video/webm";

            return MimeTypesByExtension.TryGetValue(extension, out var mimeType) ? mimeType : null;
        }

        /// <summary>Normalises an extension to a leading dot, e.g. <c>webm</c> to <c>.webm</c>.</summary>
        public static string? NormalizeExtension(string? fileExtension)
        {
            if (string.IsNullOrWhiteSpace(fileExtension))
                return null;

            var extension = fileExtension.Trim();

            return extension.StartsWith('.') ? extension : "." + extension;
        }
    }
}
