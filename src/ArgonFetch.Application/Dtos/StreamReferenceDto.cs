namespace ArgonFetch.Application.Dtos
{
    public class StreamReferenceDto
    {
        // No UrlType beside this: one list holds renditions served by both endpoints, so only
        // the per-rendition value can be right.
        public List<MediaRenditionDto> Renditions { get; set; } = [];
    }
}
