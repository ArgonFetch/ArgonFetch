using System.Net;
using System.Text;

namespace ArgonFetch.Application.Services
{
    /// <summary>
    /// What a downloaded file should say it is. Carried from the fetch that identified the media
    /// to the stream that serves it, because by then only a cache key is left.
    /// </summary>
    public record MediaTags(string? Title, string? Artist)
    {
        public static readonly MediaTags None = new(null, null);

        public bool HasAny => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Artist);
    }

    /// <summary>
    /// Names the file a download lands as.
    /// <para>
    /// Without this a caller that is not the web UI - which builds its own name from the fetch
    /// response - saves the cache key, so a folder of downloads reads as a list of hashes.
    /// </para>
    /// </summary>
    public static class MediaFileName
    {
        // Characters Windows refuses outright, plus the separators that would turn a name into a
        // path. Trimmed rather than replaced with a marker: nobody wants "AC_DC" for "AC/DC".
        private static readonly char[] Invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

        private const int MaxStemLength = 120;

        /// <summary>
        /// "Artist - Title.ext", or a plain fallback when the source named neither.
        /// </summary>
        public static string For(MediaTags tags, string extension, string fallbackStem = "download")
        {
            var stem = Sanitize(Join(tags));

            if (string.IsNullOrWhiteSpace(stem))
                stem = fallbackStem;

            return stem + extension;
        }

        /// <summary>
        /// A Content-Disposition value carrying that name.
        /// <para>
        /// Written twice: the plain filename for anything that reads only ASCII, and the RFC 5987
        /// form so a name with accents or another script survives. Clients that understand the
        /// second prefer it, and the first stops the others saving mojibake.
        /// </para>
        /// </summary>
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

            // YouTube titles are usually written "Artist - Song" already, and prefixing the
            // credit again produced "Rick Astley - Rick Astley - Never Gonna Give You Up".
            return title.StartsWith(artist, StringComparison.OrdinalIgnoreCase)
                ? title
                : $"{artist} - {title}";
        }

        private static string Sanitize(string name)
        {
            var cleaned = new StringBuilder(name.Length);

            foreach (var character in name)
            {
                // Control characters would be legal in a name and unreadable in a listing.
                if (char.IsControl(character) || Invalid.Contains(character))
                    continue;

                cleaned.Append(character);
            }

            var trimmed = cleaned.ToString().Trim();

            // Long enough to keep a real title, short enough to leave room for the extension
            // under the 255-byte limit most filesystems impose.
            if (trimmed.Length > MaxStemLength)
                trimmed = trimmed[..MaxStemLength].TrimEnd();

            // A trailing dot or space is legal in the header and unusable as a Windows filename.
            return trimmed.TrimEnd('.', ' ');
        }

        /// <summary>
        /// The name reduced to characters a header can carry literally. Quotes and backslashes go
        /// too, since they would end the quoted string early.
        /// </summary>
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
