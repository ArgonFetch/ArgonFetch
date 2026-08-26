using ArgonFetch.Application.Enums;

namespace ArgonFetch.Application.Dtos
{
    public class StreamReferenceDto
    {
        public UrlType UrlType { get; set; }

        public List<MediaRenditionDto> Renditions { get; set; } = [];
    }
}
