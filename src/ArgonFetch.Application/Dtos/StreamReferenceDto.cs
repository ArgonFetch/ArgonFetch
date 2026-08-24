using ArgonFetch.Application.Enums;

namespace ArgonFetch.Application.Dtos
{
    public class StreamReferenceDto
    {
        public string? BestQualityDescription { get; set; }
        public string? BestQualityKey { get; set; }
        public string? BestQualityFileExtension { get; set; }

        /// <summary>
        /// Media type of the bytes the stream endpoint will send. Clients pick the on-disk
        /// extension and the tagging path from this, so it describes the source container
        /// rather than a format the API wishes it were.
        /// </summary>
        public string? BestQualityMimeType { get; set; }

        public string? MediumQualityDescription { get; set; }
        public string? MediumQualityKey { get; set; }
        public string? MediumQualityFileExtension { get; set; }
        public string? MediumQualityMimeType { get; set; }

        public string? WorstQualityDescription { get; set; }
        public string? WorstQualityKey { get; set; }
        public string? WorstQualityFileExtension { get; set; }
        public string? WorstQualityMimeType { get; set; }

        public UrlType UrlType { get; set; }

        /// <summary>
        /// Every rendition on offer, best first. The three fixed rungs above are the first,
        /// middle and last of these, kept for clients written against them.
        /// </summary>
        public List<MediaRenditionDto> Renditions { get; set; } = [];
    }
}