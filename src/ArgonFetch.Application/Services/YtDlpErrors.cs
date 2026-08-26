namespace ArgonFetch.Application.Services
{
    public static class YtDlpErrors
    {
        public static bool IsDrmProtected(IEnumerable<string>? errorOutput) =>
            Mentions(errorOutput, "DRM");

        public static bool NeedsSignedInSession(IEnumerable<string>? errorOutput) =>
            Mentions(errorOutput, "login required") ||
            Mentions(errorOutput, "log in") ||
            Mentions(errorOutput, "sign in") ||
            Mentions(errorOutput, "cookies") ||
            Mentions(errorOutput, "empty media response") ||
            Mentions(errorOutput, "rate-limit reached");

        private static bool Mentions(IEnumerable<string>? errorOutput, string phrase) =>
            errorOutput?.Any(line => line?.Contains(phrase, StringComparison.OrdinalIgnoreCase) == true) == true;
    }
}
