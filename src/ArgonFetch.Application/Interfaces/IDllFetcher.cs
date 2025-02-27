using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Models;

namespace ArgonFetch.Application.Interfaces
{
    public interface IDllFetcher
    {
        /// <summary>
        /// Fetches the media information for a specified DLL using the provided options.
        /// </summary>
        /// <param name="dllName">The name of the DLL to fetch the link for.</param>
        /// <param name="dllFetcherOptions">Options to customize the fetching process.</param>
        /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
        /// <returns>The media information as a MediaInformationDto.</returns>
        Task<MediaInformationDto> FetchLinkAsync(string dllName, DllFetcherOptions dllFetcherOptions, CancellationToken cancellationToken);
    }
}
