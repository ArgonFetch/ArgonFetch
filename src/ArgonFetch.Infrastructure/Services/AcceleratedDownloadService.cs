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
        // Capped so the window below stays bounded rather than scaling with the file.
        private const int MAX_CHUNK_SIZE = 8 * 1024 * 1024; // 8MB chunks
        private const int MAX_PARALLEL_CONNECTIONS = 8; // Maximum parallel connections

        public AcceleratedDownloadService(
            IMediaHttpClients mediaHttpClients,
            ILogger<AcceleratedDownloadService> logger)
        {
            _mediaHttpClients = mediaHttpClients;
            _logger = logger;
        }

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
            var output = new OutputTracker();

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

            // Only the requested bytes, or the response will not match its Content-Range.
            var window = range ?? new ByteRange(0, contentLength.Value - 1);

            _logger.LogInformation("Starting accelerated download with {Connections} connections for {Size} bytes",
                MAX_PARALLEL_CONNECTIONS, contentLength.Value);

            try
            {
                await DownloadInChunksAsync(url, outputStream, window, progress, output, proxy, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (output.BytesWritten == 0)
            {
                _logger.LogWarning(ex, "Accelerated download failed before writing any output, falling back to single connection");
                await DownloadSingleConnectionAsync(url, outputStream, progress, output, proxy, range, cancellationToken);
            }
        }

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

            if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
            {
                return (response.Content.Headers.ContentRange?.Length, true);
            }

            return (response.Content.Headers.ContentLength, false);
        }

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

            // Offsets into the resource, not the window.
            for (var i = window.From; i <= window.To; i += chunkSize)
            {
                var end = Math.Min(i + chunkSize - 1, window.To);
                chunks.Add((i, end));
            }

            _logger.LogInformation("Downloading {ChunkCount} chunks of ~{ChunkSizeMb} MB each",
                chunks.Count, chunkSize / 1024 / 1024);

            // Sliding window: peak memory is bounded by the window, not the file size, and the
            // consumer starts receiving before the last chunk arrives.
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