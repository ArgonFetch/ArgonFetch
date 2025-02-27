using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Models;

namespace ArgonFetch.Application.Services.DDLFetcherServices
{
    public class SpotifyDllFetcherService : IDllFetcher
    {
        public string SpotifyClientId { get; set; }
        public string SpotifyClientSecret { get; set; }
        public SpotifyDllFetcherService(string spotifyClientId, string spotifyClientSecret)
        {
            SpotifyClientId = spotifyClientId;
            SpotifyClientSecret = spotifyClientSecret;
        }

        public async Task<MediaInformationDto> FetchLinkAsync(string dllName, DllFetcherOptions dllFetcherOptions)
        {
            throw new NotImplementedException();
        }
    }
}
