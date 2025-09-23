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
            try
            {
                // First, check if the server supports range requests
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
                using var headResponse = await httpClient.SendAsync(headRequest, cancellationToken);

                if (!headResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("HEAD request failed, falling back to single connection download");
                    await DownloadSingleConnectionAsync(url, outputStream, progress, cancellationToken);
                    return;
                }

                var contentLength = headResponse.Content.Headers.ContentLength;
                var acceptRanges = headResponse.Headers.AcceptRanges?.Contains("bytes") ?? false;

                if (!contentLength.HasValue || !acceptRanges)
                {
                    _logger.LogInformation("Server doesn't support range requests, using single connection");
                    await DownloadSingleConnectionAsync(url, outputStream, progress, cancellationToken);
                    return;
                }

                _logger.LogInformation("Starting accelerated download with {Connections} connections for {Size} bytes",
                    MAX_PARALLEL_CONNECTIONS, contentLength.Value);

                await DownloadInChunksAsync(url, outputStream, contentLength.Value, progress, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during accelerated download, falling back to single connection");
                await DownloadSingleConnectionAsync(url, outputStream, progress, cancellationToken);
            }
        }

        private async Task DownloadInChunksAsync(
            string url,
            Stream outputStream,
            long contentLength,
            IProgress<double>? progress,
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