namespace ArgonFetch.Domain
{
    public class UrlReference
    {
        public int Id { get; set; }
        public required string RequestUrl { get; set; }
        public required QualityDetails AudioDetails { get; set; }
        public required QualityDetails VideoDetails { get; set; }

    }
}
