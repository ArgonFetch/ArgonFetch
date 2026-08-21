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
        public async Task<IActionResult> StreamMedia([FromRoute] string key, CancellationToken cancellationToken, [FromQuery] string? format = null)
        {
            var query = new StreamMediaQuery(key, Response, cancellationToken, format);
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