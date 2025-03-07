using ArgonFetch.Application.Enums;

namespace ArgonFetch.Application.Dtos
{
    public class ResourceInformationDto
    {
        public required MediaType Type { get; set; }
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? CoverUrl { get; set; }
        public required IEnumerable<MediaInformationDto> MediaItems { get; set; }
    }
}
