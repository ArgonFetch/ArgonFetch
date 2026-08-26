using ArgonFetch.Abstractions;
using ArgonFetch.Application.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArgonFetch.Tests
{
    public class ProviderRegistryTests
    {
        [Fact]
        public void For_PicksTheProviderThatClaimsTheLink()
        {
            var registry = Registry(
                Plugin("spotify", new StubProvider("spotify", "open.spotify.com")),
                Plugin("tiktok", new StubProvider("tiktok", "tiktok.com")));

            Assert.Equal("tiktok", registry.For(new Uri("https://www.tiktok.com/@a/video/1"))?.Id);
        }

        [Fact]
        public void For_LeavesALinkNobodyClaimsToTheOrdinaryPath()
        {
            var registry = Registry(Plugin("spotify", new StubProvider("spotify", "open.spotify.com")));

            Assert.Null(registry.For(new Uri("https://www.youtube.com/watch?v=dQw4w9WgXcQ")));
        }

        [Fact]
        public void For_SettlesAClashByTheOrderTheOperatorInstalledThem()
        {
            // Two plugins wanting the same link is a decision for whoever chose them, not for
            // whichever one happened to load first.
            var registry = Registry(
                Plugin("spotify-fork", new StubProvider("spotify-fork", "open.spotify.com")),
                Plugin("spotify", new StubProvider("spotify", "open.spotify.com")));

            Assert.Equal("spotify-fork", registry.For(new Uri("https://open.spotify.com/track/1"))?.Id);
        }

        [Fact]
        public void For_TreatsAProviderThatThrowsAsNotInterested()
        {
            // CanHandle is asked of every plugin on every request, including for sources it has
            // nothing to do with, so one that throws must not break an unrelated download.
            var registry = Registry(
                Plugin("broken", new ThrowingProvider()),
                Plugin("spotify", new StubProvider("spotify", "open.spotify.com")));

            Assert.Equal("spotify", registry.For(new Uri("https://open.spotify.com/track/1"))?.Id);
        }

        [Fact]
        public void Hooks_AreCollectedFromEveryPlugin()
        {
            var registry = Registry(
                new LoadedPlugin("a", null, "1.0", [], [new StubHook()]),
                new LoadedPlugin("b", null, "1.0", [], [new StubHook()]));

            Assert.Equal(2, registry.Hooks.Count);
        }

        private static ProviderRegistry Registry(params LoadedPlugin[] plugins) =>
            new(plugins, NullLogger<ProviderRegistry>.Instance);

        private static LoadedPlugin Plugin(string id, ISourceProvider provider) =>
            new(id, null, "1.0", [provider], []);

        private sealed class StubProvider(string id, string host) : ISourceProvider
        {
            public string Id { get; } = id;

            public bool CanHandle(Uri url) => url.Host.Contains(host, StringComparison.OrdinalIgnoreCase);

            public Task<ProviderOutcome> PrepareAsync(Uri url, IProviderContext context, CancellationToken cancellationToken) =>
                Task.FromResult(ProviderOutcome.PassThrough);
        }

        private sealed class ThrowingProvider : ISourceProvider
        {
            public string Id => "broken";

            public bool CanHandle(Uri url) => throw new InvalidOperationException("bad plugin");

            public Task<ProviderOutcome> PrepareAsync(Uri url, IProviderContext context, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
        }

        private sealed class StubHook : IFetchOptionsHook
        {
            public void Configure(IFetchOptions options, Uri url) { }
        }
    }

    public class PluginInstallRequestTests
    {
        [Theory]
        [InlineData("spotify", "spotify", null)]
        [InlineData("spotify@1.2.0", "spotify", "1.2.0")]
        [InlineData("  spotify @ 1.2.0  ", "spotify", "1.2.0")]
        // A trailing separator with nothing after it asks for the newest, rather than for a
        // version named the empty string.
        [InlineData("spotify@", "spotify", null)]
        public void Parse_ReadsAnIdAndAnOptionalPinnedVersion(string request, string id, string? version)
        {
            var parsed = PluginInstaller.Parse(request);

            Assert.NotNull(parsed);
            Assert.Equal(id, parsed!.Value.Id);
            Assert.Equal(version, parsed.Value.Version);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Parse_IgnoresAnEmptyEntry(string request) =>
            Assert.Null(PluginInstaller.Parse(request));
    }
}
