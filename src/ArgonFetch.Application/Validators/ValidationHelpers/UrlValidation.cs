namespace ArgonFetch.Application.Validators.ValidationHelpers
{
    public static class UrlValidation
    {
        public static bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out _);
        }
    }
}
