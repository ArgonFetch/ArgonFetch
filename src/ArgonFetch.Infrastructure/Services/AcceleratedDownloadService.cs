using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Services;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace ArgonFetch.Infrastructure.Services
{
    public class AcceleratedDownloadService : IAcceleratedDownloadService
    {
        private readonly IMediaHttpClients _mediaHttpClients;
        private readonly ILogger<AcceleratedDownloadService> _logger;
        private const int MIN_CHUNK_SIZE = 2 * 1024 * 1024; // 2MB chunks
        // Chunk size is capped so the sliding window below stays bounded. Without a cap it
        // scales with the file, and the window along with it - a 2GB download would hold
        // roughly 1GB in memory.
        private const int MAX_CHUNK_SIZE = 8 * 1024 * 1024; // 8MB chunks
        private const int MAX_PARALLEL_CONNECTIONS = 8; // Maximum parallel connections

        public AcceleratedDownloadService(
            IMediaHttpClients mediaHttpClients,
            ILogger<AcceleratedDownloadService> logger)
        {
            _mediaHttpClients = mediaHttpClients;
            _logger = logger;
        }

        /// <summary>
        /// Length the upstream reports, or null when it does not report one. A failure here is
        /// not fatal - the caller simply cannot declare Content-Length and the response is chunked.
        /// </summary>
        public async Task<long?> GetContentLengthAsync(
            string url,
            string? proxy = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (contentLength, _) = await ProbeAsync(url, proxy, cancellationToken);

                if (contentLength == null)
                {
                    _logger.LogWarning("Probe did not report a content length for {Url}", url);
                }

                return contentLength;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not determine content length for {Url}", url);
                return null;
            }
        }

        public async Task StreamWithAccelerationAsync(
            string url,
            Stream outputStream,
            IProgress<double>? progress = null,
            string? proxy = null,
            ByteRange? range = null,
            CancellationToken cancellationToken = default)
        {
            // Counts what has already been handed to the caller. Once any byte is written,
            // restarting the download would append a second copy of the file, so a failure
            // past that point has to propagate rather than fall back.
            var output = new OutputTracker();

            // Probe for range support first. This writes nothing, so failing here is
            // always safe to recover from.
            long? contentLength = null;
            var acceptsRanges = false;
            var probeSucceeded = false;

            try
            {
                (contentLength, acceptsRanges) = await ProbeAsync(url, proxy, cancellationToken);
                probeSucceeded = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Range support probe failed, falling back to single connection download");
            }

            if (!probeSucceeded || !contentLength.HasValue || !acceptsRanges)
            {
                if (probeSucceeded)
                {
                    _logger.LogInformation("Server doesn't support range requests, using single connection");
                }

                await DownloadSingleConnectionAsync(url, outputStream, progress, output, proxy, range, cancellationToken);
                return;
            }

            // Serving a window rather than the file: the caller is seeking or resuming, and
            // only the requested bytes may be written or the response will not line up with
            // the Content-Range it announced.
            var window = range ?? new ByteRange(0, contentLength.Value - 1);

            _logger.LogInformation("Starting accelerated download with {Connections} connections for {Size} bytes",
                MAX_PARALLEL_CONNECTIONS, contentLength.Value);

            try
            {
                await DownloadInChunksAsync(url, outputStream, window, progress, output, proxy, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The caller went away or the request was aborted; retrying is pointless.
                throw;
            }
            catch (Exception ex) when (output.BytesWritten == 0)
            {
                // Nothing has reached the caller yet, so starting over is safe.
                _logger.LogWarning(ex, "Accelerated download failed before writing any output, falling back to single connection");
                await DownloadSingleConnectionAsync(url, outputStream, progress, output, proxy, range, cancellationToken);
            }
        }

        /// <summary>
        /// Asks for the first byte to learn the resource's length and whether it serves ranges.
        /// <para>
        /// A HEAD request would be the obvious probe, but media hosts answer 403 to both HEAD
        /// and an unranged GET for signed URLs, so the only shape that reliably works is the
        /// ranged GET the chunked download uses anyway.
        /// </para>
        /// </summary>
        private async Task<(long? ContentLength, bool AcceptsRanges)> ProbeAsync(
            string url,
            string? proxy,
            CancellationToken cancellationToken)
        {
            var httpClient = _mediaHttpClients.For(proxy);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, 0);

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            response.EnsureSuccessStatusCode();

            // 206 answers with the total after the slash in "bytes 0-0/12345"; a host that
            // ignored the range answered 200 and reported the whole length instead.
            if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
            {
                return (response.Content.Headers.ContentRange?.Length, true);
            }

            return (response.Content.Headers.ContentLength, false);
        }

        /// <summary>
        /// Tracks how much of the output stream has already been written, so callers can tell
        /// whether restarting a transfer would duplicate bytes the consumer has already seen.
        /// </summary>
        private sealed class OutputTracker
        {
            public long BytesWritten { get; private set; }

            public void Add(long count) => BytesWritten += count;
        }

        private async Task DownloadInChunksAsync(
            string url,
            Stream outputStream,
            ByteRange window,
            IProgress<double>? progress,
            OutputTracker output,
            string? proxy,
            CancellationToken cancellationToken)
        {
            var contentLength = window.Length;
            var chunkSize = Math.Clamp(contentLength / (MAX_PARALLEL_CONNECTIONS * 2), MIN_CHUNK_SIZE, MAX_CHUNK_SIZE);
            var chunks = new List<(long start, long end)>();

            // Chunks are offsets into the resource, not into the window, so a request for the
            // tail of a file still asks the source for the right bytes.
            for (var i = window.From; i <= window.To; i += chunkSize)
            {
                var end = Math.Min(i + chunkSize - 1, window.To);
                chunks.Add((i, end));
            }

            _logger.LogInformation("Downloading {ChunkCount} chunks of ~{ChunkSizeMb} MB each",
                chunks.Count, chunkSize / 1024 / 1024);

            // Sliding window: at most MAX_PARALLEL_CONNECTIONS chunks are downloading or
            // waiting to be written at any moment, so peak memory is bounded by the window
            // rather than by the size of the file. Chunks are written in order and their
            // buffers released as soon as they reach the output stream, which also means the
            // consumer starts receiving data before the last chunk has arrived.
            var inFlight = new Queue<Task<byte[]>>(MAX_PARALLEL_CONNECTIONS);
            var nextToStart = 0;
            var totalBytesDownloaded = 0L;

            try
            {
                for (var index = 0; index < chunks.Count; index++)
                {
                    while (inFlight.Count < MAX_PARALLEL_CONNECTIONS && nextToStart < chunks.Count)
                    {
                        var chunk = chunks[nextToStart++];
                        inFlight.Enqueue(DownloadChunkAsync(url, chunk.start, chunk.end, proxy, cancellationToken));
                    }

                    var data = await inFlight.Dequeue();

                    // Counted before the write: a write that throws may still have pushed
                    // bytes to the consumer, so the output can no longer be restarted.
                    output.Add(data.Length);
                    await outputStream.WriteAsync(data, 0, data.Length, cancellationToken);

                    totalBytesDownloaded += data.Length;
                    progress?.Report((double)totalBytesDownloaded / contentLength);

                    _logger.LogDebug("Wrote chunk {Index} ({Start}-{End})",
                        index, chunks[index].start, chunks[index].end);
                }
            }
            catch
            {
                // Observe the downloads still running so their failures don't resurface
                // later as unobserved task exceptions.
                foreach (var pending in inFlight)
                {
                    _ = pending.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
                }

                throw;
            }

            await outputStream.FlushAsync(cancellationToken);
            _logger.LogInformation("Accelerated download completed successfully");
        }

        private async Task<byte[]> DownloadChunkAsync(
            string url,
            long rangeStart,
            long rangeEnd,
            string? proxy,
            CancellationToken cancellationToken)
        {
            var httpClient = _mediaHttpClients.For(proxy);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(rangeStart, rangeEnd);

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync();
        }

        private async Task DownloadSingleConnectionAsync(
            string url,
            Stream outputStream,
            IProgress<double>? progress,
            OutputTracker output,
            string? proxy,
            ByteRange? range,
            CancellationToken cancellationToken)
        {
            var httpClient = _mediaHttpClients.For(proxy);

            // Ranged from the first byte: an unranged GET is refused for signed media URLs.
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = range.HasValue
                ? new RangeHeaderValue(range.Value.From, range.Value.To)
                : new RangeHeaderValue(0, null);

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            using var stream = await response.Content.ReadAsStreamAsync();

            var buffer = new byte[81920];
            var totalBytesRead = 0L;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
            {
                output.Add(bytesRead);
                await outputStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalBytesRead += bytesRead;

                if (contentLength.HasValue && progress != null)
                {
                    progress.Report((double)totalBytesRead / contentLength.Value);
                }
            }

            await outputStream.FlushAsync(cancellationToken);
        }
    }
}