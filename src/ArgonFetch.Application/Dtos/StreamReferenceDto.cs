using ArgonFetch.Application.Enums;

namespace ArgonFetch.Application.Dtos
{
    public class StreamReferenceDto
    {
        public string? BestQualityDescription { get; set; }
        public string? BestQualityKey { get; set; }
        public string? BestQualityFileExtension { get; set; }

        public string? MediumQualityDescription { get; set; }
        public string? MediumQualityKey { get; set; }
        public string? MediumQualityFileExtension { get; set; }

        public string? WorstQualityDescription { get; set; }
        public string? WorstQualityKey { get; set; }
        public string? WorstQualityFileExtension { get; set; }

        public UrlType UrlType { get; set; }
    }
}