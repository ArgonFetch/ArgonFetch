namespace ArgonFetch.Application.Dtos
{
    public class CombinedStreamUrlDto
    {
        public string? BestQuality { get; set; }
        public string? BestQualityDescription { get; set; }

        public string? MediumQuality { get; set; }
        public string? MediumQualityDescription { get; set; }

        public string? WorstQuality { get; set; }
        public string? WorstQualityDescription { get; set; }
    }
}