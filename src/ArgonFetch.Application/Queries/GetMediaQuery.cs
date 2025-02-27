using AngleSharp;
using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Models;
using ArgonFetch.Application.Services;
using ArgonFetch.Application.Services.DDLFetcherServices;
using MediatR;
using SpotifyAPI.Web;

namespace ArgonFetch.Application.Queries
{
    public class GetMediaQuery : IRequest<MediaInformationDto>
    {
        public GetMediaQuery(string query)
        {
            Query = query;
        }

        public string Query { get; set; }
    }

    public class GetMediaQueryHandler : IRequestHandler<GetMediaQuery, MediaInformationDto>
    {
        private readonly SpotifyClient _spotifyClient;
        private readonly DllFetcherService _dllFetcherService;
        private readonly TikTokDllFetcherService _tikTokDllFetcher;
        private readonly HttpClient _httpClient;
        private readonly YTMusicAPI.SearchClient _ytmSearchClient;

        public GetMediaQueryHandler(SpotifyClient spotifyClient, DllFetcherService dllFetcherService, HttpClient httpClient, YTMusicAPI.SearchClient ytmSearchClient, TikTokDllFetcherService tikTokDllFetcher)
        {
            _spotifyClient = spotifyClient;
            _dllFetcherService = dllFetcherService;
            _httpClient = httpClient;
            _ytmSearchClient = ytmSearchClient;
            _tikTokDllFetcher = tikTokDllFetcher;
        }

        public async Task<MediaInformationDto> Handle(GetMediaQuery request, CancellationToken cancellationToken)
        {
            var platform = PlatformIdentifierService.IdentifyPlatform(request.Query);

            switch (platform)
            {
                case Platform.Spotify:
                    return await HandleSpotify(request.Query, cancellationToken);
                case Platform.YouTube:
                    return await _dllFetcherService.FetchLinkAsync(request.Query, cancellationToken: cancellationToken);
                case Platform.SoundCloud:
                    return await HandleSoundCloud(request.Query);
                case Platform.SearchTerm:
                    return await _dllFetcherService.FetchLinkAsync(request.Query, cancellationToken: cancellationToken);
                case Platform.TikTok:
                    return await _tikTokDllFetcher.FetchLinkAsync(request.Query, cancellationToken: cancellationToken);
                case Platform.Unknown:
                    return await _dllFetcherService.FetchLinkAsync(request.Query, cancellationToken: cancellationToken);

                default:
                    throw new NotSupportedException($"Platform {platform} is not supported.");
            }
        }

        private async Task<MediaInformationDto> HandleSpotify(string query, CancellationToken cancellationToken)
        {
            var uri = new Uri(query);
            var segments = uri.Segments;
            var searchResponse = await _spotifyClient.Tracks.Get(segments.Last());

            if (searchResponse == null)
                throw new ArgumentException("Track not found");

            var response = await _ytmSearchClient.SearchTracksAsync(new YTMusicAPI.Model.QueryRequest
            {
                Query = $"{searchResponse.Name} by {searchResponse.Artists.First().Name}"
            }, cancellationToken);

            var ytmTrackUrl = response.Result.First().Url;

            var downloadOptions = new DllFetcherOptions { MediaFormat = MediaFormat.BestAudio };

            var streamingUrl = (await _dllFetcherService.FetchLinkAsync(ytmTrackUrl, downloadOptions, cancellationToken)).StreamingUrl;

            return new MediaInformationDto
            {
                RequestedUrl = query,
                StreamingUrl = streamingUrl,
                CoverUrl = searchResponse.Album.Images.First().Url,
                Title = searchResponse.Name,
                Author = searchResponse.Artists.First().Name
            };
        }

        private async Task<MediaInformationDto> HandleSoundCloud(string query)
        {
            var parsedUrl = new Uri(query);
            var subdomain = parsedUrl.Host.Split('.')[0];

            if (subdomain.Contains("api"))
            {
                var response = await _httpClient.GetStringAsync($"https://w.soundcloud.com/player/?url={query}");
                var context = BrowsingContext.New(Configuration.Default);
                var document = await context.OpenAsync(req => req.Content(response));
                var canonicalLink = document.QuerySelector("link[rel='canonical']");
                var href = canonicalLink.GetAttribute("href");

                return await _dllFetcherService.FetchLinkAsync(href);
            }

            throw new NotSupportedException("Only API SoundCloud URLs are supported.");
        }

    }
}
