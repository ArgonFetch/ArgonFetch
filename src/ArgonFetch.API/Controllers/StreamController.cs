using ArgonFetch.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ArgonFetch.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StreamController : ControllerBase
    {
        private readonly IMediator _mediator;
        private readonly ILogger<StreamController> _logger;

        public StreamController(IMediator mediator, ILogger<StreamController> logger)
        {
            _mediator = mediator;
            _logger = logger;
        }

        [HttpGet("Combined/{key}", Name = "Combined")]
        // Muxed on the fly, so its length is unknown and it cannot be seeked within.
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> StreamCombinedMedia([FromRoute] string key, CancellationToken cancellationToken)
        {
            var query = new StreamCombinedMediaQuery(key, Response, cancellationToken);
            var result = await _mediator.Send(query, cancellationToken);

            // Handle the result if response hasn't started
            if (!Response.HasStarted && !result.IsSuccess && result.StatusCode.HasValue)
            {
                Response.StatusCode = result.StatusCode.Value;
                await Response.WriteAsync(result.ErrorMessage ?? "An error occurred");
                return new EmptyResult();
            }

            return new EmptyResult();
        }

        /// <param name="format">
        /// Optional container to convert to, currently only "mp3". Omit it to receive the
        /// source untouched, which is faster and avoids a re-encode.
        /// </param>
        [HttpGet("Media/{key}", Name = "Media")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status206PartialContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
        public async Task<IActionResult> StreamMedia([FromRoute] string key, CancellationToken cancellationToken, [FromQuery] string? format = null)
        {
            // The Range header is read here rather than in the handler so the query stays
            // something that can be constructed without an HttpRequest.
            var query = new StreamMediaQuery(key, Response, cancellationToken, format, Request.Headers.Range);
            var result = await _mediator.Send(query, cancellationToken);

            // Handle the result if response hasn't started
            if (!Response.HasStarted && !result.IsSuccess && result.StatusCode.HasValue)
            {
                Response.StatusCode = result.StatusCode.Value;
                await Response.WriteAsync(result.ErrorMessage ?? "An error occurred");
                return new EmptyResult();
            }

            return new EmptyResult();
        }
    }
}