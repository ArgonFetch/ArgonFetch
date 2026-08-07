using ArgonFetch.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Http.Headers;

namespace ArgonFetch.Infrastructure.Services
{
    public class AcceleratedDownloadService : IAcceleratedDownloadService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AcceleratedDownloadService> _logger;
        private const int DEFAULT_CHUNK_SIZE = 2 * 1024 * 1024; // 2MB chunks
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
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

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
            var chunkSize = Math.Max(DEFAULT_CHUNK_SIZE, contentLength / (MAX_PARALLEL_CONNECTIONS * 2));
            var chunks = new List<(long start, long end)>();

            // Calculate chunks
            for (long i = 0; i < contentLength; i += chunkSize)
            {
                var end = Math.Min(i + chunkSize - 1, contentLength - 1);
                chunks.Add((i, end));
            }

            _logger.LogInformation("Downloading {ChunkCount} chunks of ~{ChunkSize} bytes each",
                chunks.Count, chunkSize / 1024 / 1024);

            // Download chunks in parallel
            var chunkData = new ConcurrentDictionary<int, byte[]>();
            var totalBytesDownloaded = 0L;

            using var semaphore = new SemaphoreSlim(MAX_PARALLEL_CONNECTIONS);
            var downloadTasks = chunks.Select(async (chunk, index) =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var data = await DownloadChunkAsync(url, chunk.start, chunk.end, cancellationToken);
                    chunkData[index] = data;

                    var downloaded = Interlocked.Add(ref totalBytesDownloaded, data.Length);
                    progress?.Report((double)downloaded / contentLength);

                    _logger.LogDebug("Downloaded chunk {Index} ({Start}-{End})", index, chunk.start, chunk.end);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            await Task.WhenAll(downloadTasks);

            // Write chunks to output stream in order
            for (int i = 0; i < chunks.Count; i++)
            {
                if (chunkData.TryGetValue(i, out var data))
                {
                    // Counted before the write: a write that throws may still have pushed
                    // bytes to the consumer, so the output can no longer be restarted.
                    output.Add(data.Length);
                    await outputStream.WriteAsync(data, 0, data.Length, cancellationToken);
                }
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
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

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
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

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