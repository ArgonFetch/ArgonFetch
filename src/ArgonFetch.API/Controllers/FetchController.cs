using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Services;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace ArgonFetch.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FetchController : ControllerBase
    {
        private readonly IMediator _mediator;
        private readonly ILogger<FetchController> _logger;
        private readonly IMaintenanceState _maintenance;

        public FetchController(IMediator mediator, ILogger<FetchController> logger, IMaintenanceState maintenance)
        {
            _mediator = mediator;
            _logger = logger;
            _maintenance = maintenance;
        }

        [HttpGet("GetResource", Name = "GetResource")]
        [ProducesResponseType(typeof(ResourceInformationDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
        public async Task<ActionResult<ResourceInformationDto>> GetResource([FromQuery][Required] string url)
        {
            if (_maintenance.Activity is { } activity)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
                {
                    Title = activity,
                    Detail = "The server is briefly unavailable while it updates itself. Try again in a moment.",
                    Status = StatusCodes.Status503ServiceUnavailable
                });
            }

            try
            {
                var result = await _mediator.Send(new GetMediaQuery(url));

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Resource not found for {Url}", url);

                return NotFound(new ProblemDetails
                {
                    Title = "Resource Not Found",
                    Status = StatusCodes.Status404NotFound
                });
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning(ex, "Unsupported media type for {Url}", url);

                return StatusCode(StatusCodes.Status415UnsupportedMediaType, new ProblemDetails
                {
                    Title = "Unsupported Media Type",
                    Detail = ex.Message,
                    Status = StatusCodes.Status415UnsupportedMediaType
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch {Url}", url);

                return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
                {
                    Title = "Fetch Failed",
                    Status = StatusCodes.Status502BadGateway
                });
            }
        }

        /// <summary>
        /// Resolves a link into streams meant to be played rather than saved: video and audio
        /// apart, each one served by /api/Stream/Media, which declares a length and answers range
        /// requests. A player needs both of those to seek, and the muxed renditions GetResource
        /// returns can offer neither.
        /// </summary>
        [HttpGet("GetPlayback", Name = "GetPlayback")]
        [ProducesResponseType(typeof(PlaybackInformationDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
        public async Task<ActionResult<PlaybackInformationDto>> GetPlayback([FromQuery][Required] string url)
        {
            if (_maintenance.Activity is { } activity)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
                {
                    Title = activity,
                    Detail = "The server is briefly unavailable while it updates itself. Try again in a moment.",
                    Status = StatusCodes.Status503ServiceUnavailable
                });
            }

            try
            {
                return Ok(await _mediator.Send(new GetPlaybackQuery(url)));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Playback resource not found for {Url}", url);

                return NotFound(new ProblemDetails
                {
                    Title = "Resource Not Found",
                    Status = StatusCodes.Status404NotFound
                });
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning(ex, "Unsupported media type for playback of {Url}", url);

                return StatusCode(StatusCodes.Status415UnsupportedMediaType, new ProblemDetails
                {
                    Title = "Unsupported Media Type",
                    Detail = ex.Message,
                    Status = StatusCodes.Status415UnsupportedMediaType
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve playback for {Url}", url);

                return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
                {
                    Title = "Fetch Failed",
                    Status = StatusCodes.Status502BadGateway
                });
            }
        }
    }
}
