namespace ArgonFetch.Application.Models
{
    public class VideoInfo
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string Uploader { get; set; }
        public string Thumbnail { get; set; }
        public List<ThumbnailInfo> Thumbnails { get; set; }
    }
}
