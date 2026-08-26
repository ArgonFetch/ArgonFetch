using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Services;
using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace ArgonFetch.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StreamController : ControllerBase
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        private static readonly TimeSpan MaxWatchTime = TimeSpan.FromMinutes(30);

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        private readonly IMediator _mediator;
        private readonly IArchiveProgressTracker _archiveProgress;
        private readonly ILogger<StreamController> _logger;

        public StreamController(IMediator mediator, IArchiveProgressTracker archiveProgress, ILogger<StreamController> logger)
        {
            _mediator = mediator;
            _archiveProgress = archiveProgress;
            _logger = logger;
        }

        [HttpGet("Combined/{key}", Name = "Combined")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> StreamCombinedMedia([FromRoute] string key, CancellationToken cancellationToken)
        {
            var query = new StreamCombinedMediaQuery(key, Response, cancellationToken);
            var result = await _mediator.Send(query, cancellationToken);

            if (!Response.HasStarted && !result.IsSuccess && result.StatusCode.HasValue)
            {
                Response.StatusCode = result.StatusCode.Value;
                await Response.WriteAsync(result.ErrorMessage ?? "An error occurred");
                return new EmptyResult();
            }

            return new EmptyResult();
        }

        [HttpGet("Archive", Name = "Archive")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> StreamArchive(
            [FromQuery][Required] string url,
            CancellationToken cancellationToken,
            [FromQuery] string? jobId = null)
        {
            // ZipArchive writes its central directory synchronously and Kestrel refuses that,
            // which truncated every archive. The media itself still goes out asynchronously.
            var bodyControl = HttpContext.Features.Get<IHttpBodyControlFeature>();

            if (bodyControl is not null)
                bodyControl.AllowSynchronousIO = true;

            var query = new StreamArchiveQuery(url, jobId, Response, cancellationToken);
            var result = await _mediator.Send(query, cancellationToken);

            if (!Response.HasStarted && !result.IsSuccess && result.StatusCode.HasValue)
            {
                Response.StatusCode = result.StatusCode.Value;
                await Response.WriteAsync(result.ErrorMessage ?? "An error occurred");
                return new EmptyResult();
            }

            return new EmptyResult();
        }

        [HttpGet("Archive/Progress/{jobId}", Name = "ArchiveProgress")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task ArchiveProgress([FromRoute] string jobId, CancellationToken cancellationToken)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            var deadline = DateTimeOffset.UtcNow.Add(MaxWatchTime);
            ArchiveProgress? last = null;

            while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
            {
                var current = _archiveProgress.Get(jobId);

                if (current is not null && current != last)
                {
                    await Response.WriteAsync($"data: {JsonSerializer.Serialize(current, JsonOptions)}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);

                    last = current;

                    if (current.IsFinished)
                        return;
                }

                await Task.Delay(PollInterval, cancellationToken);
            }
        }

        [HttpGet("Media/{key}", Name = "Media")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status206PartialContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
        public async Task<IActionResult> StreamMedia([FromRoute] string key, CancellationToken cancellationToken, [FromQuery] string? format = null)
        {
            var query = new StreamMediaQuery(key, Response, cancellationToken, format, Request.Headers.Range);
            var result = await _mediator.Send(query, cancellationToken);

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