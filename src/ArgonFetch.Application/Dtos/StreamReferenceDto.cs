using ArgonFetch.Application.Enums;

namespace ArgonFetch.Application.Dtos
{
    /// <summary>
    /// The downloadable versions of one media item, and which stream endpoint serves them.
    /// </summary>
    public class StreamReferenceDto
    {
        /// <summary>Which stream endpoint serves these - muxing is not the same route as passing bytes through.</summary>
        public UrlType UrlType { get; set; }

        /// <summary>
        /// Every rendition on offer, best first.
        /// <para>
        /// This used to sit beside a fixed "best", "medium" and "worst" triple, which was the
        /// first, middle and last of this list. Those three were whatever happened to fall at
        /// the ends and the middle of the source's format list, so the same label meant a
        /// different thing from one item to the next, and a source offering eleven versions was
        /// described with the same three rungs as one offering two. Callers pick from the list.
        /// </para>
        /// </summary>
        public List<MediaRenditionDto> Renditions { get; set; } = [];
    }
}
