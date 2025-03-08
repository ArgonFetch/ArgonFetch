namespace ArgonFetch.Application.Models
{
    public class ProxyRangeResponse
    {
        public byte[] Data { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
    }
}
