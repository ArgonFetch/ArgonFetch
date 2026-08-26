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
