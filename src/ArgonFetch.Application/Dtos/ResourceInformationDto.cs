using ArgonFetch.Application.Enums;

namespace ArgonFetch.Application.Dtos
{
    public class ResourceInformationDto
    {
        public required MediaType Type { get; set; }

        /// <summary>
        /// The link this was resolved from. Echoed back because a collection is addressed by it
        /// elsewhere - the archive endpoint takes it - and the individual entries carry only
        /// their own links, not the one that produced the listing.
        /// </summary>
        public string? RequestedUrl { get; set; }

        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? CoverUrl { get; set; }
        public required IEnumerable<MediaInformationDto> MediaItems { get; set; }
    }
}
