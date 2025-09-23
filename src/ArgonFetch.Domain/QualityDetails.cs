namespace ArgonFetch.Domain
{
    public class QualityDetails
    {
        public int Id { get; set; }

        public string? BestQualityDescription { get; set; }
        public string? BestQuality { get; set; }
        public string? BestQualityFileExtension { get; set; }

        public string? MediumQualityDescription { get; set; }
        public string? MediumQuality { get; set; }
        public string? MediumQualityFileExtension { get; set; }

        public string? WorstQualityDescription { get; set; }
        public string? WorstQuality { get; set; }
        public string? WorstQualityFileExtension { get; set; }
    }
}
