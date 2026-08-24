namespace ArgonFetch.Application.Services
{
    /// <summary>
    /// One candidate format, stripped of the extractor's own types so the builders and this
    /// picker do not have to know where it came from.
    /// </summary>
    public record RenditionSource(
        string Url,
        string? Description,
        string? Extension,
        int? Height,
        double? Bitrate,
        long? FileSizeBytes);

    /// <summary>
    /// Chooses which formats to offer. Sources list a dozen or more renditions that differ in
    /// ways nobody picking a download cares about, so this reduces them to a handful of
    /// genuinely different steps, best first.
    /// </summary>
    public static class RenditionPicker
    {
        /// <summary>How many renditions to offer per source.</summary>
        public const int DefaultCount = 4;

        /// <summary>
        /// Video renditions, best first.
        /// </summary>
        /// <param name="perContainer">
        /// Whether the source container reaches the caller. It does for formats served as they
        /// are, and there WebM beside MP4 at one resolution is a real choice. Muxed video is
        /// always delivered as MP4, so distinguishing sources there would offer the same
        /// resolution twice under the same label.
        /// </param>
        public static List<RenditionSource> PickVideo(
            IEnumerable<RenditionSource> candidates,
            int count = DefaultCount,
            bool perContainer = false)
        {
            var streamable = candidates.Where(c => !string.IsNullOrEmpty(c.Url));

            var groups = perContainer
                ? streamable.GroupBy(c => (c.Height ?? 0, Container(c)))
                : streamable.GroupBy(c => (c.Height ?? 0, string.Empty));

            var byResolution = groups
                // MP4 sources win a tie: their codecs are the ones an MP4 is expected to carry,
                // and muxing VP9 into one produces a file some players refuse.
                .Select(group => group
                    .OrderByDescending(c => Container(c) == ".mp4")
                    .ThenByDescending(c => c.Bitrate ?? 0)
                    .First())
                .OrderByDescending(c => c.Height ?? 0)
                .ThenByDescending(c => c.Bitrate ?? 0)
                .ToList();

            return Spread(byResolution, count);
        }

        /// <summary>
        /// Audio renditions, one per audible step per container.
        /// <para>
        /// Candidates are expected in the caller's own order of preference and stay in it: the
        /// caller knows that Opus beats AAC at the same bitrate, and re-sorting on the raw
        /// number here would quietly undo that. Bitrates are bucketed to the nearest 10 kbps,
        /// so two encodes of the same step collapse - but only within one container, because
        /// WebM and M4A at the same bitrate are a real choice for whoever has to play the file.
        /// </para>
        /// </summary>
        public static List<RenditionSource> PickAudio(IEnumerable<RenditionSource> candidates, int count = DefaultCount)
        {
            var distinctSteps = candidates
                .Where(c => !string.IsNullOrEmpty(c.Url))
                .GroupBy(c => (Container(c), (int)Math.Round((c.Bitrate ?? 0) / 10)))
                .Select(group => group.First())
                .ToList();

            return Spread(distinctSteps, count);
        }

        private static string Container(RenditionSource source) =>
            MediaFormats.NormalizeExtension(source.Extension)?.ToLowerInvariant() ?? string.Empty;

        /// <summary>
        /// Takes <paramref name="count"/> entries spread evenly across the list, always keeping
        /// the first and last. Taking the top N instead would offer four near-identical high
        /// quality options and no small one, which is the choice people actually want.
        /// </summary>
        private static List<RenditionSource> Spread(List<RenditionSource> ordered, int count)
        {
            if (count < 1 || ordered.Count == 0)
                return [];

            if (ordered.Count <= count)
                return ordered;

            if (count == 1)
                return [ordered[0]];

            var picked = new List<RenditionSource>(count);

            for (var i = 0; i < count; i++)
            {
                // Rounded rather than truncated so the steps stay even, and the last index is
                // exactly the last entry.
                var index = (int)Math.Round(i * (ordered.Count - 1) / (double)(count - 1));

                if (!picked.Contains(ordered[index]))
                {
                    picked.Add(ordered[index]);
                }
            }

            return picked;
        }

        /// <summary>Label for a picker: the resolution for video, the bitrate for audio.</summary>
        public static string Label(RenditionSource source, bool isAudio)
        {
            if (!isAudio && source.Height is > 0)
            {
                var height = source.Height.Value;

                // The exact line count stays, with the familiar name beside it: someone looking
                // for 4K should not have to know that it means 2160p.
                var tier = height switch
                {
                    >= 4320 => " (8K)",
                    >= 2160 => " (4K)",
                    _ => string.Empty
                };

                return $"{height}p{tier}";
            }

            if (source.Bitrate is > 0)
                return $"{Math.Round(source.Bitrate.Value)} kbps";

            // Nothing quantitative to show; the source's own wording is better than nothing.
            return string.IsNullOrWhiteSpace(source.Description) ? "Unknown" : source.Description;
        }
    }
}
