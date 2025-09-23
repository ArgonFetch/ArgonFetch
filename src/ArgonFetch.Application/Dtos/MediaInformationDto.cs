namespace ArgonFetch.Application.Dtos
{
    public class MediaInformationDto
    {
        public required string RequestedUrl { get; set; }

        // Video with audio (either pre-muxed or will be combined via FFmpeg if needed)
        public StreamingUrlDto? Video { get; set; }

        // Audio-only option
        public StreamingUrlDto? Audio { get; set; }

        public required string CoverUrl { get; set; }
        public required string Title { get; set; }
        public required string Author { get; set; }
    }
}
