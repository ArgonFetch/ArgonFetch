using ArgonFetch.Application.Services;
using ArgonFetch.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArgonFetch.Tests
{
    public class RequestCounterTests : IDisposable
    {
        private readonly string _directory;
        private readonly DataPaths _paths;

        public RequestCounterTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "argonfetch-counter-" + Guid.NewGuid().ToString("N"));
            _paths = new DataPaths(_directory);
        }

        private RequestCounterService NewService() =>
            new(_paths, NullLogger<RequestCounterService>.Instance);

        [Fact]
        public async Task GetTotal_IsZero_WhenNothingHasBeenServedYet()
        {
            using var counter = NewService();

            Assert.Equal(0, await counter.GetTotalAsync());
        }

        [Fact]
        public async Task Increment_CountsEveryRequest()
        {
            using var counter = NewService();

            for (var i = 0; i < 3; i++)
                await counter.IncrementAsync();

            Assert.Equal(3, await counter.GetTotalAsync());
        }

        [Fact]
        public async Task Increment_DoesNotLoseCountsRaisedConcurrently()
        {
            using var counter = NewService();

            // The whole reason the increment is interlocked: requests arrive in parallel.
            await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => counter.IncrementAsync()));

            Assert.Equal(200, await counter.GetTotalAsync());
        }

        [Fact]
        public async Task Total_SurvivesARestart()
        {
            var first = NewService();
            await first.StartAsync(CancellationToken.None);
            await first.IncrementAsync();
            await first.IncrementAsync();
            await first.StopAsync(CancellationToken.None);
            first.Dispose();

            using var second = NewService();

            Assert.Equal(2, await second.GetTotalAsync());
        }

        [Fact]
        public async Task Total_ContinuesFromThePersistedCount_RatherThanRestarting()
        {
            var first = NewService();
            await first.StartAsync(CancellationToken.None);
            await first.IncrementAsync();
            await first.StopAsync(CancellationToken.None);
            first.Dispose();

            using var second = NewService();
            await second.IncrementAsync();

            Assert.Equal(2, await second.GetTotalAsync());
        }

        [Fact]
        public async Task Load_StartsFromZero_WhenTheFileIsUnreadable()
        {
            Directory.CreateDirectory(_directory);
            await File.WriteAllTextAsync(_paths.RequestCounterPath, "{ not json");

            // A damaged stats file must not take the app down with it.
            using var counter = NewService();

            Assert.Equal(0, await counter.GetTotalAsync());
        }

        [Fact]
        public async Task Stop_WritesNoStrayTempFileBesideTheCounter()
        {
            var counter = NewService();
            await counter.StartAsync(CancellationToken.None);
            await counter.IncrementAsync();
            await counter.StopAsync(CancellationToken.None);
            counter.Dispose();

            Assert.True(File.Exists(_paths.RequestCounterPath));
            Assert.False(File.Exists(_paths.RequestCounterPath + ".tmp"));
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
