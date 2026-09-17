using ArgonFetch.Application.Dtos;

namespace ArgonFetch.Application.Services
{
    /// <summary>One format as the source describes it, before it becomes a playable track.</summary>
    public record PlaybackSource(
        string Url,
        string? Description,
        string? Extension,
        string? VideoCodec,
        string? AudioCodec,
        int? Width,
        int? Height,
        double? Fps,
        double? Bitrate,
        long? FileSizeBytes);

    /// <summary>
    /// Turns formats into the quality ladder a player offers. Unlike <see cref="RenditionPicker"/>
    /// this keeps every step the source has: a download menu with fifteen entries is noise, but a
    /// player switching quality wants each one, and the steps cost nothing until they are chosen.
    /// </summary>
    public static class PlaybackTrackPicker
    {
        public static List<PlaybackSource> PickVideo(IEnumerable<PlaybackSource> candidates) =>
            candidates
                .Where(candidate => !string.IsNullOrEmpty(candidate.Url))
                // One per resolution and container: the same picture twice is a menu entry that
                // decides nothing.
                .GroupBy(candidate => (candidate.Height ?? 0, Container(candidate)))
                .Select(group => group.OrderByDescending(candidate => candidate.Bitrate ?? 0).First())
                .OrderByDescending(candidate => candidate.Height ?? 0)
                .ThenByDescending(candidate => PreferredContainer(candidate) ? 1 : 0)
                .ThenByDescending(candidate => candidate.Bitrate ?? 0)
                .ToList();

        public static List<PlaybackSource> PickAudio(IEnumerable<PlaybackSource> candidates) =>
            candidates
                .Where(candidate => !string.IsNullOrEmpty(candidate.Url))
                .GroupBy(candidate => (Container(candidate), (int)Math.Round((candidate.Bitrate ?? 0) / 10)))
                .Select(group => group.First())
                .OrderByDescending(candidate => candidate.Bitrate ?? 0)
                .ToList();

        /// <summary>
        /// The type a media element is given. A bare <c>video/webm</c> makes Chrome accept a file
        /// it cannot decode and fail silently on the first frame, so the codecs travel with it.
        /// </summary>
        public static string ContentType(string mimeType, string? videoCodec, string? audioCodec)
        {
            var codecs = new[] { Normalize(videoCodec), Normalize(audioCodec) }
                .Where(codec => codec is not null)
                .ToArray();

            return codecs.Length == 0
                ? mimeType
                : $"{mimeType}; codecs=\"{string.Join(", ", codecs)}\"";
        }

        /// <summary>The codecs alone, for a client that builds its own type string.</summary>
        public static string? Codec(string? videoCodec, string? audioCodec)
        {
            var codecs = new[] { Normalize(videoCodec), Normalize(audioCodec) }
                .Where(codec => codec is not null)
                .ToArray();

            return codecs.Length == 0 ? null : string.Join(", ", codecs);
        }

        public static string Label(PlaybackSource source, bool isAudio)
        {
            if (!isAudio && source.Height is > 0)
            {
                var height = source.Height.Value;

                var tier = height switch
                {
                    >= 4320 => " (8K)",
                    >= 2160 => " (4K)",
                    _ => string.Empty
                };

                var smooth = source.Fps is > 40 ? Math.Round(source.Fps.Value).ToString("0") : null;

                return smooth is null ? $"{height}p{tier}" : $"{height}p{smooth}{tier}";
            }

            if (source.Bitrate is > 0)
                return $"{Math.Round(source.Bitrate.Value)} kbps";

            return string.IsNullOrWhiteSpace(source.Description) ? "Unknown" : source.Description;
        }

        private static string? Normalize(string? codec)
        {
            if (string.IsNullOrWhiteSpace(codec) || codec.Equals("none", StringComparison.OrdinalIgnoreCase))
                return null;

            return codec.Trim();
        }

        private static string Container(PlaybackSource source) =>
            MediaFormats.NormalizeExtension(source.Extension)?.ToLowerInvariant() ?? string.Empty;

        // Every browser decodes H.264 in MP4; VP9 and AV1 depend on the machine. When a resolution
        // exists in both, the one that always plays is listed first.
        private static bool PreferredContainer(PlaybackSource source) => Container(source) == ".mp4";
    }
}
