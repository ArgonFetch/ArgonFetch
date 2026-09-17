namespace ArgonFetch.Application.Dtos
{
    /// <summary>
    /// What a player needs, which is not what a downloader needs: every track here is a single
    /// stream served by <c>/api/Stream/Media</c>, so it declares a length and answers range
    /// requests. The muxed renditions <c>GetResource</c> offers cannot do either - FFmpeg writes
    /// them as the client reads them - and a video element that cannot seek is not a player.
    /// Video and audio arrive apart, exactly as the source stores them, and the client pairs them.
    /// </summary>
    public class PlaybackInformationDto
    {
        public required string RequestedUrl { get; set; }

        public required string Title { get; set; }

        public required string Author { get; set; }

        public string? CoverUrl { get; set; }

        /// <summary>Length of the media, when the source states one.</summary>
        public double? DurationSeconds { get; set; }

        /// <summary>Video-only tracks, highest resolution first.</summary>
        public List<PlaybackTrackDto> Video { get; set; } = [];

        /// <summary>Audio-only tracks, best first.</summary>
        public List<PlaybackTrackDto> Audio { get; set; } = [];

        /// <summary>
        /// Tracks that already carry both, for clients that would rather not pair two elements.
        /// Sources are dropping these, so the list is often empty.
        /// </summary>
        public List<PlaybackTrackDto> Muxed { get; set; } = [];
    }
}
