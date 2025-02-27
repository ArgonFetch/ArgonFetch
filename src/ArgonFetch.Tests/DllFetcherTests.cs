using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Models;
using ArgonFetch.Application.Services.DDLFetcherServices;

namespace ArgonFetch.Tests
{
    public class DllFetcherTests
    {
        [Fact]
        public async Task FetchLinkAsync_ReturnsMediaInformationDto()
        {
            // Arrange
            var dllFetcherService = new DllFetcherService();
            var dllName = "https://music.youtube.com/watch?v=kA_1chVgPVA&si=SCujMuH6O3FmAjJQ";
            var dllFetcherOptions = new DllFetcherOptions
            {
                MediaFormat = MediaFormat.Best
            };
            var cancellationToken = CancellationToken.None;

            // Act
            var result = await dllFetcherService.FetchLinkAsync(dllName, dllFetcherOptions, cancellationToken);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(dllName, result.RequestedUrl);
            Assert.False(string.IsNullOrEmpty(result.StreamingUrl));
            Assert.False(string.IsNullOrEmpty(result.CoverUrl));
            Assert.Equal("CRAZY", result.Title);
            Assert.Equal("LE SSERAFIM", result.Author);
        }
    }
}
