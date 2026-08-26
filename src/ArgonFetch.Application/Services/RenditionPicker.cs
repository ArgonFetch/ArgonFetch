namespace ArgonFetch.Application.Services
{
    public record RenditionSource(
        string Url,
        string? Description,
        string? Extension,
        int? Height,
        double? Bitrate,
        long? FileSizeBytes);

    public static class RenditionPicker
    {
        public const int DefaultCount = 4;

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
                .Select(group => group
                    .OrderByDescending(c => Container(c) == ".mp4")
                    .ThenByDescending(c => c.Bitrate ?? 0)
                    .First())
                .OrderByDescending(c => c.Height ?? 0)
                .ThenByDescending(c => c.Bitrate ?? 0)
                .ToList();

            return Spread(byResolution, count);
        }

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
                var index = (int)Math.Round(i * (ordered.Count - 1) / (double)(count - 1));

                if (!picked.Contains(ordered[index]))
                {
                    picked.Add(ordered[index]);
                }
            }

            return picked;
        }

        public static string Label(RenditionSource source, bool isAudio)
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

                return $"{height}p{tier}";
            }

            if (source.Bitrate is > 0)
                return $"{Math.Round(source.Bitrate.Value)} kbps";

            return string.IsNullOrWhiteSpace(source.Description) ? "Unknown" : source.Description;
        }
    }
}
