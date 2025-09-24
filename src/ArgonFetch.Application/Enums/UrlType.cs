namespace ArgonFetch.Application.Enums
{
    public enum UrlType
    {
        Combined = 0,    // Video with audio combined (either pre-muxed or FFmpeg combined)
        Media = 1        // Single stream (video-only or audio-only)
    }
}