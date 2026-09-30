using ArgonFetch.Abstractions;
using ArgonFetch.Application.Plugins;
using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using YoutubeDLSharp.Options;

namespace ArgonFetch.Tests
{
    public class DeclinedLinkTests
    {
        private const string Track = "https://open.spotify.com/track/1cdVCfArJfCHexHBy45DzY";

        [Fact]
        public async Task ALinkThePluginFoundNothingFor_IsNotFound_NotDrmProtected()
        {
            var handler = Handler(new DecliningProvider());

            await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new GetMediaQuery(Track), default).AsTask());
        }

        [Fact]
        public async Task ALinkNoPluginClaims_StillReportsWhatYtDlpSaid()
        {
            var handler = Handler(provider: null);

            await Assert.ThrowsAsync<NotSupportedException>(() => handler.Handle(new GetMediaQuery(Track), default).AsTask());
        }

        // Nothing past the provider and yt-dlp is reached before the refusal, so the rest stays unset.
        private static GetMediaQueryHandler Handler(ISourceProvider? provider) => new(
            new DrmRefusingFetcher(),
            null!, null!, null!, null!, null!,
            new OneProviderRegistry(provider),
            new NoContextFactory(),
            NullLogger<GetMediaQueryHandler>.Instance);

        private sealed class DrmRefusingFetcher : IMediaMetadataFetcher
        {
            public Task<MediaMetadata> FetchAsync(string query, OptionSet? options = null) =>
                throw new NotSupportedException("This media is DRM protected and cannot be downloaded.");
        }

        private sealed class DecliningProvider : ISourceProvider
        {
            public string Id => "spotify";

            public IReadOnlyList<string> UrlPatterns => [@"^https?://([\w-]+\.)*spotify\.com/"];

            public Task<ProviderOutcome> PrepareAsync(Uri url, IProviderContext context, CancellationToken cancellationToken) =>
                Task.FromResult(ProviderOutcome.Declined);
        }

        private sealed class OneProviderRegistry(ISourceProvider? provider) : IProviderRegistry
        {
            public ISourceProvider? For(Uri url) => provider;

            public IReadOnlyList<IFetchOptionsHook> Hooks => [];

            public IReadOnlyList<LoadedPlugin> Plugins => [];
        }

        private sealed class NoContextFactory : IProviderContextFactory
        {
            public IProviderContext For(string pluginId, Func<Uri, CancellationToken, Task<ProbeResult?>> probe) => null!;
        }
    }
}
