using ArgonFetch.Application.Enums;

namespace ArgonFetch.Application.Services
{
    public static class PlatformIdentifierService
    {
        public static Platform IdentifyPlatform(string queryUrl)
        {
            Uri uri;
            try
            {
                uri = new Uri(queryUrl);
            }
            catch (UriFormatException)
            {
                return Platform.SearchTerm;
            }

            // Only the sources this application fetches by itself are named here. Anything a
            // plugin handles was already dealt with before this was ever asked, and anything
            // nobody handles is Unknown - which yt-dlp is welcome to try and report on.
            string hostname = uri.Host.ToLower();

            switch (hostname)
            {
                case string h when h.Contains("youtube") || h.Contains("youtu"):
                    return Platform.YouTube;
                case string h when h.Contains("soundcloud"):
                    return Platform.SoundCloud;
                default:
                    return Platform.Unknown;
            }
        }
    }
}
