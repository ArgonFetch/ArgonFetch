using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Models;

namespace ArgonFetch.Application.Interfaces
{
    public interface IDllFetcher
    {
        /// <summary>
        /// Fetches the download link for a specified DLL using the provided options.
        /// </summary>
        /// <param name="dllName">The name of the DLL to fetch the link for.</param>
        /// <param name="dllFetcherOptions">Options to customize the fetching process.</param>
        /// <returns>The download link as a string.</returns>
        Task<MediaInformationDto> FetchLinkAsync(string dllName, DllFetcherOptions dllFetcherOptions);
    }
}
