using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Services;
using MediatR;
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
        // Marked required rather than merely being non-nullable: binding already refused a
        // call without it, but the generated schema said the parameter was optional, so every
        // client built from it offered a request the API will only ever answer with a 400.
        public async Task<ActionResult<ResourceInformationDto>> GetResource([FromQuery][Required] string url)
        {
            // Refused rather than attempted: the yt-dlp binary is being replaced underneath us,
            // and the error that produces looks like a broken source rather than a busy server.
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
                    // Carried through so a caller learns which kind of unsupported this is -
                    // DRM, or a link shape ArgonFetch does not handle yet.
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

        //[HttpGet("DownloadResource", Name = "DownloadResource")]
        //[ProducesResponseType(typeof(ResourceInformationDto), StatusCodes.Status200OK)]
        //[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        //[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        //[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
        //[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
        //public async Task<ActionResult<ResourceInformationDto>> DownloadResource(string url)
        //{
        //    try
        //    {
        //        var result = await _mediator.Send(new DownloadMediaQuery(url));
        //        return Ok(result);
        //    }
        //    catch (ArgumentException ex)
        //    {
        //        return NotFound(new ProblemDetails
        //        {
        //            Title = "Resource Not Found",
        //            Status = StatusCodes.Status404NotFound
        //        });
        //    }
        //    catch (NotSupportedException ex)
        //    {
        //        return StatusCode(StatusCodes.Status415UnsupportedMediaType, new ProblemDetails
        //        {
        //            Title = "Unsupported Media Type",
        //            Status = StatusCodes.Status415UnsupportedMediaType
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
        //        {
        //            Title = "Fetch Failed",
        //            Status = StatusCodes.Status502BadGateway
        //        });
        //    }
        //}
    }
}
