namespace ArgonFetch.Abstractions
{
    /// <summary>
    /// A source ArgonFetch does not already know how to fetch.
    /// <para>
    /// yt-dlp is what fetches things the ordinary way, and most links need nothing more than
    /// that. A provider exists for the links that do: one that has to be turned into a different
    /// link before anything can be downloaded, one that lists a collection, or one that a
    /// separate piece of code fetches entirely on its own.
    /// </para>
    /// </summary>
    public interface ISourceProvider
    {
        /// <summary>
        /// Stable name for this provider, matching the id it is installed under - "spotify".
        /// <para>
        /// It is how an operator asks for the plugin and how conflicts between two providers are
        /// settled, so renaming one is a breaking change to somebody's configuration.
        /// </para>
        /// </summary>
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

        /// <summary>
        /// A second opinion, once a pattern has already matched.
        /// <para>
        /// Almost no provider needs this - the patterns are usually the whole answer, and the
        /// default accepts whatever they matched. Override it when the URL alone decides
        /// something a regular expression should not be asked to express.
        /// </para>
        /// <para>
        /// Must be cheap, must not touch the network, and must not throw: it is asked on every
        /// request for a link this provider claimed. That is why it is not asynchronous - an
        /// awaitable signature invites a request nobody meant to make.
        /// </para>
        /// </summary>
        bool CanHandle(Uri url) => true;

        /// <summary>
        /// What should happen to the link. See <see cref="ProviderOutcome"/> for the four answers.
        /// </summary>
        Task<ProviderOutcome> PrepareAsync(Uri url, IProviderContext context, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Adjusts how yt-dlp is invoked, for sources that are fetched the ordinary way but need
    /// something said first - a session to prove who is asking, a particular container.
    /// <para>
    /// Separate from <see cref="ISourceProvider"/> because it composes: several hooks may apply
    /// to one fetch, where exactly one provider ever handles a link.
    /// </para>
    /// </summary>
    public interface IFetchOptionsHook
    {
        /// <summary>
        /// Called before every fetch, for every link. Change only what this hook is for and
        /// leave the rest alone - the next hook in line is looking at the same object.
        /// </summary>
        void Configure(IFetchOptions options, Uri url);
    }

    /// <summary>
    /// The parts of a yt-dlp invocation a plugin may change. Deliberately not the whole option
    /// set: everything exposed here is something the host promises to keep meaning the same.
    /// </summary>
    public interface IFetchOptions
    {
        /// <summary>Path to a cookies file, for a source that serves nothing to strangers.</summary>
        string? CookiesPath { get; set; }

        /// <summary>Proxy to fetch through. Already set from the rotating pool; replacing it opts out of that.</summary>
        string? Proxy { get; set; }

        /// <summary>yt-dlp format selector, when the default pick is wrong for a source.</summary>
        string? Format { get; set; }

        /// <summary>An <c>--extractor-args</c> entry, e.g. <c>("youtube", "player_client=web")</c>.</summary>
        void SetExtractorArgument(string extractor, string argument);
    }
}
