namespace ArgonFetch.Application.Dtos
{
    public class MediaInformationDto
    {
        public required string RequestedUrl { get; set; }

        // Video with audio (either pre-muxed or will be combined via FFmpeg if needed)
        public StreamReferenceDto? Video { get; set; }

        // Audio-only option
        public StreamReferenceDto? Audio { get; set; }

        /// <summary>Cover art, where the source has any.</summary>
        public string? CoverUrl { get; set; }
        public required string Title { get; set; }
        public required string Author { get; set; }
    }
}
