namespace ArgonFetch.Application.Dtos
{
    public class MediaInformationDto
    {
        public required string RequestedUrl { get; set; }

        public StreamReferenceDto? Video { get; set; }

        public StreamReferenceDto? Audio { get; set; }

        public string? CoverUrl { get; set; }
        public required string Title { get; set; }
        public required string Author { get; set; }
    }
}
