using System.Text.Json;
using ArgonFetch.Application.Services;
using ArgonFetch.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Infrastructure.Services
{
    /// <summary>
    /// Keeps the request counter up to date.
    /// <para>
    /// The count lives in memory and is mirrored to a small JSON file, because a total
    /// advertised as "requests this installation has served" is not that if it resets on every
    /// restart. Writes are debounced - the file is rewritten at most once per
    /// <see cref="FlushInterval"/>, plus once on shutdown - so a busy instance does not turn
    /// every counter bump into a disk write. A hard kill loses at most one interval's worth.
    /// </para>
    /// </summary>
    public class RequestCounterService : IRequestCounterService, IHostedService, IDisposable
    {
        private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(10);

        private readonly string _filePath;
        private readonly ILogger<RequestCounterService> _logger;

        // Guards the file itself: the timer and shutdown can both decide to flush.
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        private long _totalRequests;
        private long _lastRequestAtUtcTicks;
        private int _dirty;

        private Timer? _flushTimer;

        public RequestCounterService(IDataPaths dataPaths, ILogger<RequestCounterService> logger)
        {
            _filePath = dataPaths.RequestCounterPath;
            _logger = logger;

            Load();
        }

        public Task IncrementAsync(CancellationToken cancellationToken = default)
        {
            // Interlocked rather than a lock: concurrent requests cannot overwrite each
            // other's count, and the increment stays off the download's critical path.
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Exchange(ref _lastRequestAtUtcTicks, DateTime.UtcNow.Ticks);
            Interlocked.Exchange(ref _dirty, 1);

            return Task.CompletedTask;
        }

        public Task<long> GetTotalAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Interlocked.Read(ref _totalRequests));

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _flushTimer = new Timer(_ => _ = FlushIfDirtyAsync(), null, FlushInterval, FlushInterval);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_flushTimer is not null)
            {
                await _flushTimer.DisposeAsync();
                _flushTimer = null;
            }

            await FlushIfDirtyAsync();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    _logger.LogInformation(
                        "No request counter at {Path} yet, starting from zero", _filePath);
                    return;
                }

                var counter = JsonSerializer.Deserialize<RequestCounter>(File.ReadAllText(_filePath));
                if (counter is null)
                    return;

                _totalRequests = counter.TotalRequests;
                _lastRequestAtUtcTicks = counter.LastRequestAtUtc.Ticks;
            }
            catch (Exception ex)
            {
                // Starting from zero beats refusing to boot over a stats file.
                _logger.LogWarning(ex, "Could not read the request counter from {Path}", _filePath);
            }
        }

        private async Task FlushIfDirtyAsync()
        {
            if (Interlocked.Exchange(ref _dirty, 0) == 0)
                return;

            await _writeLock.WaitAsync();
            try
            {
                var counter = new RequestCounter
                {
                    TotalRequests = Interlocked.Read(ref _totalRequests),
                    LastRequestAtUtc = new DateTime(
                        Interlocked.Read(ref _lastRequestAtUtcTicks), DateTimeKind.Utc)
                };

                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

                // Written beside the real file and moved into place, so a crash mid-write
                // leaves the previous count intact rather than a truncated file.
                var tempPath = _filePath + ".tmp";
                await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(counter));
                File.Move(tempPath, _filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                // A counter is not worth failing a download over, and the in-memory count
                // carries on regardless - only the persisted copy falls behind.
                Interlocked.Exchange(ref _dirty, 1);
                _logger.LogWarning(ex, "Could not persist the request counter to {Path}", _filePath);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Dispose()
        {
            _flushTimer?.Dispose();
            _writeLock.Dispose();
        }
    }
}
