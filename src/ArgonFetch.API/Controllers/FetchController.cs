using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ArgonFetch.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FetchController : ControllerBase
    {
        private readonly IMediator _mediator;
        private readonly ILogger<FetchController> _logger;

        public FetchController(IMediator mediator, ILogger<FetchController> logger)
        {
            _mediator = mediator;
            _logger = logger;
        }

        [HttpGet("GetResource", Name = "GetResource")]
        [ProducesResponseType(typeof(ResourceInformationDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<ResourceInformationDto>> GetResource(string url)
        {
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
