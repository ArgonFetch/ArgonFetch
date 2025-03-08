namespace ArgonFetch.Application.Models
{
    public class ProxyHeadResponse
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public Dictionary<string, IEnumerable<string>> Headers { get; set; }
        public long? ContentLength { get; set; }
    }
}
