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
            Mentions(errorOutput, "DRM");

        /// <summary>
        /// Whether the source refused because nobody was signed in. Instagram answers this way
        /// for practically everything now, and an age-gated YouTube video does the same, so the
        /// answer is a cookies file rather than a different link.
        /// </summary>
        public static bool NeedsSignedInSession(IEnumerable<string>? errorOutput) =>
            Mentions(errorOutput, "login required") ||
            Mentions(errorOutput, "log in") ||
            Mentions(errorOutput, "sign in") ||
            Mentions(errorOutput, "cookies") ||
            // Instagram's own wording when it serves nothing to a signed-out request.
            Mentions(errorOutput, "empty media response") ||
            Mentions(errorOutput, "rate-limit reached");

        private static bool Mentions(IEnumerable<string>? errorOutput, string phrase) =>
            errorOutput?.Any(line => line?.Contains(phrase, StringComparison.OrdinalIgnoreCase) == true) == true;
    }
}
