using System.ComponentModel;
using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Services;
using Mediator;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ArgonFetch.API.Mcp
{
    /// <summary>
    /// The MCP face of the API. It sends the same query <see cref="Controllers.FetchController"/>
    /// does, so there is one resolution path, and resolves the rendition key into a URL the caller
    /// can simply GET - which the REST response leaves to the caller.
    /// </summary>
    [McpServerToolType]
    public class ArgonFetchTools(
        IMediator mediator,
        IHttpContextAccessor httpContextAccessor,
        IMaintenanceState maintenance,
        ILogger<ArgonFetchTools> logger)
    {
        [McpServerTool(Name = "fetch_media", ReadOnly = true, OpenWorld = true)]
        [Description("""
            Resolve a media link (YouTube, TikTok, Spotify, SoundCloud, Instagram and anything else
            yt-dlp handles) into its title, author and download URLs.

            Every rendition carries a downloadUrl that can be fetched directly - no second call, no
            parameters to add. Those URLs stop working an hour after this call, so resolve again
            rather than reusing an old result.

            A playlist answers with one entry per track in `items`, each with a title and a
            `sourceUrl` but no renditions - call this again with that `sourceUrl` to get the
            download URLs for a track.
            """)]
        public async Task<object> FetchMediaAsync(
            [Description("The page URL to resolve, for example https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
            string url,
            CancellationToken cancellationToken)
        {
            if (maintenance.Activity is { } activity)
                throw new McpException($"{activity}. The server is briefly unavailable while it updates itself; try again in a moment.");

            ResourceInformationDto resource;

            try
            {
                resource = await mediator.Send(new GetMediaQuery(url), cancellationToken);
            }
            catch (ArgumentException)
            {
                throw new McpException($"Nothing was found at {url}.");
            }
            catch (NotSupportedException ex)
            {
                // DRM, or a link shape ArgonFetch does not handle. The reason is the useful part.
                throw new McpException(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MCP fetch failed for {Url}", url);

                throw new McpException($"Could not fetch {url}.");
            }

            var request = httpContextAccessor.HttpContext!.Request;
            var origin = $"{request.Scheme}://{request.Host}";

            return new
            {
                Type = resource.Type.ToString(),
                resource.Title,
                resource.Author,
                resource.CoverUrl,
                Items = resource.MediaItems.Select(item => new
                {
                    item.Title,
                    item.Author,
                    SourceUrl = item.RequestedUrl,
                    item.CoverUrl,
                    Video = Downloads(origin, item.Video),
                    Audio = Downloads(origin, item.Audio)
                })
            };
        }

        private static object[] Downloads(string origin, StreamReferenceDto? reference) =>
            reference?.Renditions.Select(object (rendition) => new
            {
                rendition.Label,
                rendition.Description,
                rendition.FileExtension,
                rendition.FileSizeBytes,
                DownloadUrl = DownloadUrlBuilder.UrlFor(origin, rendition)
            }).ToArray() ?? [];
    }
}
