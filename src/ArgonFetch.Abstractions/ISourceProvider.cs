namespace ArgonFetch.Abstractions
{
    public interface ISourceProvider
    {
        string Id { get; }

        // Matched case-insensitively against the whole URL. One that will not compile is skipped.
        IReadOnlyList<string> UrlPatterns { get; }

        bool CanHandle(Uri url) => true;

        Task<ProviderOutcome> PrepareAsync(Uri url, IProviderContext context, CancellationToken cancellationToken);
    }

    public interface IFetchOptionsHook
    {
        void Configure(IFetchOptions options, Uri url);
    }

    public interface IFetchOptions
    {
        string? CookiesPath { get; set; }

        string? Proxy { get; set; }

        string? Format { get; set; }

        void SetExtractorArgument(string extractor, string argument);
    }
}
