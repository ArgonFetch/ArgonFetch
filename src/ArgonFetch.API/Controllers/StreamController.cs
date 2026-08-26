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
        // Often enough to look live against tracks that take seconds each, seldom enough that a
        // watcher costs nothing to keep open.
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        // A watcher whose archive never reports again is dropped rather than held forever.
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

        /// <summary>
        /// Every track of a playlist or album, as one zip.
        /// </summary>
        /// <param name="url">Link to the collection, the same one the fetch endpoint takes.</param>
        [HttpGet("Archive", Name = "Archive")]
        // Built while it is sent, so it declares no length and cannot be seeked within.
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        /// <param name="jobId">
        /// Optional id to publish progress against, chosen by the caller. Pass the same one to
        /// the progress endpoint to watch the archive being built. Omit it and nothing is
        /// published, which is what a plain download does not need.
        /// </param>
        public async Task<IActionResult> StreamArchive(
            [FromQuery][Required] string url,
            CancellationToken cancellationToken,
            [FromQuery] string? jobId = null)
        {
            // Closing a zip entry writes its data descriptor, and closing the archive writes the
            // central directory - both synchronously, with no async path offered for either, so
            // Kestrel's default refusal truncates every archive. Allowed for this response alone;
            // the bytes of the media itself still go out asynchronously.
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


        /// <summary>
        /// How far along an archive is, as server-sent events, until it finishes.
        /// </summary>
        /// <param name="jobId">The id passed to the archive request whose progress to follow.</param>
        [HttpGet("Archive/Progress/{jobId}", Name = "ArchiveProgress")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task ArchiveProgress([FromRoute] string jobId, CancellationToken cancellationToken)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            // Nginx buffers a response by default, which holds every event back until the end and
            // makes a live progress feed arrive all at once when there is nothing left to show.
            Response.Headers["X-Accel-Buffering"] = "no";

            var deadline = DateTimeOffset.UtcNow.Add(MaxWatchTime);
            ArchiveProgress? last = null;

            while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
            {
                var current = _archiveProgress.Get(jobId);

                // Sent only when something changed, so a slow track does not fill the connection
                // with repetitions of the same number.
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