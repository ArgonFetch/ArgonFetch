namespace ArgonFetch.Application.Dtos
{
    /// <summary>
    /// One subtitle track, served by the instance rather than linked to at the source.
    /// </summary>
    /// <remarks>
    /// The source's own caption URLs are signed and increasingly refuse anyone but the player that
    /// asked for them - YouTube answers a browser with <c>200</c> and an empty body. The instance
    /// can fetch them, so it does, and hands over a path of its own.
    /// </remarks>
    public class PlaybackSubtitleDto
    {
        public required string Key { get; set; }

        /// <summary>Path under the instance that serves this track as WebVTT.</summary>
        public required string Path { get; set; }

        /// <summary>BCP-47 tag, as the source labels it.</summary>
        public required string Language { get; set; }

        /// <summary>What to show in a menu - "English", "Deutsch (Deutschland)".</summary>
        public required string Name { get; set; }

        /// <summary>Written by the source's own speech recognition rather than by a person.</summary>
        public bool Automatic { get; set; }
    }
}
