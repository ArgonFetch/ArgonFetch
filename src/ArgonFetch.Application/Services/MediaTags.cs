using System.Net;
using System.Text;

namespace ArgonFetch.Application.Services
{
    public record MediaTags(string? Title, string? Artist)
    {
        public static readonly MediaTags None = new(null, null);

        public bool HasAny => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Artist);
    }

    public static class MediaFileName
    {
        // Characters Windows refuses outright, plus the separators that would turn a name into a
        // path. Trimmed rather than replaced with a marker: nobody wants "AC_DC" for "AC/DC".
        private static readonly char[] Invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

        private const int MaxStemLength = 120;

        public static string For(MediaTags tags, string extension, string fallbackStem = "download")
        {
            var stem = Sanitize(Join(tags));

            if (string.IsNullOrWhiteSpace(stem))
                stem = fallbackStem;

            return stem + extension;
        }

        public static string ContentDisposition(MediaTags tags, string extension, string fallbackStem = "download")
        {
            var fileName = For(tags, extension, fallbackStem);
            var ascii = AsciiFold(fileName);
            var encoded = Uri.EscapeDataString(fileName);

            return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{encoded}";
        }

        private static string Join(MediaTags tags)
        {
            var artist = tags.Artist?.Trim();
            var title = tags.Title?.Trim();

            if (string.IsNullOrWhiteSpace(title))
                return artist ?? string.Empty;

            if (string.IsNullOrWhiteSpace(artist))
                return title;

            return title.StartsWith(artist, StringComparison.OrdinalIgnoreCase)
                ? title
                : $"{artist} - {title}";
        }

        private static string Sanitize(string name)
        {
            var cleaned = new StringBuilder(name.Length);

            foreach (var character in name)
            {
                if (char.IsControl(character) || Invalid.Contains(character))
                    continue;

                cleaned.Append(character);
            }

            var trimmed = cleaned.ToString().Trim();

            if (trimmed.Length > MaxStemLength)
                trimmed = trimmed[..MaxStemLength].TrimEnd();

            // A trailing dot or space is legal in the header and unusable as a Windows filename.
            return trimmed.TrimEnd('.', ' ');
        }

        private static string AsciiFold(string name)
        {
            var folded = new StringBuilder(name.Length);

            foreach (var character in name)
            {
                if (character is >= ' ' and <= '~' && character != '"' && character != '\\')
                {
                    folded.Append(character);
                }
                else if (folded.Length == 0 || folded[^1] != '_')
                {
                    folded.Append('_');
                }
            }

            var result = folded.ToString().Trim('_', ' ');

            return string.IsNullOrEmpty(result) ? "download" : result;
        }

    }
}
