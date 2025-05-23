namespace ArgonFetch.Application.Dtos
{
    public class MediaInformationDto
    {
        public required string RequestedUrl { get; set; }
        public required string StreamingUrl { get; set; }
        public required string CoverUrl { get; set; }
        public required string Title { get; set; }
        public required string Author { get; set; }
        public string? FileExtension { get; set; }
    }
}
