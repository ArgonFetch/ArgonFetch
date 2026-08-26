namespace ArgonFetch.Application.Services
{
    public static class MediaFormats
    {
        public const int Mp3BitrateKbps = 192;

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

        public static string? ExtensionFor(string? mimeType)
        {
            if (string.IsNullOrWhiteSpace(mimeType))
                return null;

            var bare = mimeType.Split(';')[0].Trim();

            if (bare.Equals("audio/webm", StringComparison.OrdinalIgnoreCase) ||
                bare.Equals("video/webm", StringComparison.OrdinalIgnoreCase))
                return ".webm";

            foreach (var (extension, knownType) in MimeTypesByExtension)
            {
                if (bare.Equals(knownType, StringComparison.OrdinalIgnoreCase))
                    return extension;
            }

            return null;
        }

        public static string? NormalizeExtension(string? fileExtension)
        {
            if (string.IsNullOrWhiteSpace(fileExtension))
                return null;

            var extension = fileExtension.Trim();

            return extension.StartsWith('.') ? extension : "." + extension;
        }
    }
}
