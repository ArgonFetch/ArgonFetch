using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Services;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace ArgonFetch.Infrastructure.Services
{
    public class AcceleratedDownloadService : IAcceleratedDownloadService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AcceleratedDownloadService> _logger;
        private const int MIN_CHUNK_SIZE = 2 * 1024 * 1024; // 2MB chunks
        // Chunk size is capped so the sliding window below stays bounded. Without a cap it
        // scales with the file, and the window along with it - a 2GB download would hold
        // roughly 1GB in memory.
        private const int MAX_CHUNK_SIZE = 8 * 1024 * 1024; // 8MB chunks
        private const int MAX_PARALLEL_CONNECTIONS = 8; // Maximum parallel connections

        public AcceleratedDownloadService(
            IHttpClientFactory httpClientFactory,
            ILogger<AcceleratedDownloadService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<Stream> DownloadWithAccelerationAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            var memoryStream = new MemoryStream();
            await StreamWithAccelerationAsync(url, memoryStream, null, cancellationToken);
            memoryStream.Position = 0;
            return memoryStream;
        }

        public async Task StreamWithAccelerationAsync(
            string url,
            Stream outputStream,
            IProgress<double>? progress = null,
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
                var httpClient = _httpClientFactory.CreateClient(MediaHttpClientDefaults.ClientName);

                using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
                using var headResponse = await httpClient.SendAsync(headRequest, cancellationToken);

                if (headResponse.IsSuccessStatusCode)
                {
                    contentLength = headResponse.Content.Headers.ContentLength;
                    acceptsRanges = headResponse.Headers.AcceptRanges?.Contains("bytes") ?? false;
                    probeSucceeded = true;
                }
                else
                {
                    _logger.LogWarning("HEAD request failed with {StatusCode}, falling back to single connection download",
                        (int)headResponse.StatusCode);
                }
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

                await DownloadSingleConnectionAsync(url, outputStream, progress, output, cancellationToken);
                return;
            }

            _logger.LogInformation("Starting accelerated download with {Connections} connections for {Size} bytes",
                MAX_PARALLEL_CONNECTIONS, contentLength.Value);

            try
            {
                await DownloadInChunksAsync(url, outputStream, contentLength.Value, progress, output, cancellationToken);
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
                await DownloadSingleConnectionAsync(url, outputStream, progress, output, cancellationToken);
            }
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
            long contentLength,
            IProgress<double>? progress,
            OutputTracker output,
            CancellationToken cancellationToken)
        {
            var chunkSize = Math.Clamp(contentLength / (MAX_PARALLEL_CONNECTIONS * 2), MIN_CHUNK_SIZE, MAX_CHUNK_SIZE);
            var chunks = new List<(long start, long end)>();

            // Calculate chunks
            for (long i = 0; i < contentLength; i += chunkSize)
            {
                var end = Math.Min(i + chunkSize - 1, contentLength - 1);
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
                        inFlight.Enqueue(DownloadChunkAsync(url, chunk.start, chunk.end, cancellationToken));
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
            CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient(MediaHttpClientDefaults.ClientName);

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
            CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient(MediaHttpClientDefaults.ClientName);

            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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