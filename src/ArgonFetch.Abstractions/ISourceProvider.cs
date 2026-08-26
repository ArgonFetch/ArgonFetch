namespace ArgonFetch.Abstractions
{
    public interface ISourceProvider
    {
        string Id { get; }

        /// <summary>
        /// Which links this provider wants, as regular expressions matched against the whole URL.
        /// <para>
        /// Declared rather than decided in code so the host can do the matching: it compiles each
        /// one once instead of on every request, applies a time limit so a pattern that backtracks
        /// badly cannot hang a download, and can say plainly which plugin claimed what. It also
        /// means the usual plugin writes one line here and no matching code at all.
        /// </para>
        /// <para>
        /// Matched without regard to case. A pattern that does not compile is skipped with a line
        /// in the log rather than taking the plugin down with it.
        /// </para>
        /// </summary>
        /// <example><c>[@"^https?://([\w-]+\.)*spotify\.com/"]</c></example>
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
