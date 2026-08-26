using ArgonFetch.Application.Services;

namespace ArgonFetch.Tests
{
    public class MaintenanceStateTests
    {
        [Fact]
        public void Activity_IsNull_WhenNothingIsRunning()
        {
            Assert.Null(new MaintenanceState().Activity);
        }

        [Fact]
        public void Activity_ReportsTheRunningWork_AndClearsWhenItFinishes()
        {
            var state = new MaintenanceState();

            using (state.Begin("Updating yt-dlp"))
            {
                Assert.Equal("Updating yt-dlp", state.Activity);
            }

            Assert.Null(state.Activity);
        }

        [Fact]
        public void Activity_SurvivesUntilTheLastOverlappingScopeEnds()
        {
            var state = new MaintenanceState();

            var first = state.Begin("Updating yt-dlp");
            var second = state.Begin("Migrating database");

            first.Dispose();

            Assert.Equal("Migrating database", state.Activity);

            second.Dispose();

            Assert.Null(state.Activity);
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            var state = new MaintenanceState();

            var scope = state.Begin("Updating yt-dlp");
            using var other = state.Begin("Updating yt-dlp");

            scope.Dispose();
            scope.Dispose();

            Assert.Equal("Updating yt-dlp", state.Activity);
        }
    }
}
