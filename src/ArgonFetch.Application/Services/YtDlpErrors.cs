namespace ArgonFetch.Application.Services
{
    /// <summary>
    /// Reads what yt-dlp printed when a fetch failed, so the API can say why rather than
    /// treating every failure as a missing resource.
    /// </summary>
    public static class YtDlpErrors
    {
        /// <summary>
        /// Whether the source refused because the media is DRM protected - SoundCloud does this
        /// for licensed tracks. The track exists and the link is right, so reporting it as
        /// missing sends the reader looking for a mistake that is not there.
        /// </summary>
        public static bool IsDrmProtected(IEnumerable<string>? errorOutput) =>
            errorOutput?.Any(line => line?.Contains("DRM", StringComparison.OrdinalIgnoreCase) == true) == true;
    }
}
