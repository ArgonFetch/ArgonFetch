using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Application.Services
{
    /// <summary>yt-dlp's own refusal, passed on as the binary would have printed it.</summary>
    public class YtDlpFailure(string message) : Exception(message);

    /// <summary>
    /// A Python process that keeps yt-dlp imported, since starting it costs more than most lookups.
    /// </summary>
    /// <remarks>
    /// Anything wrong with the process itself answers null, and the caller runs the yt-dlp binary
    /// as before: a broken worker costs speed, never an answer.
    /// </remarks>
    public class YtDlpWorker : IDisposable
    {
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

        private readonly ILogger<YtDlpWorker> _logger;
        private readonly SemaphoreSlim _starting = new(1, 1);
        private readonly ConcurrentDictionary<long, TaskCompletionSource<(bool Ok, string Payload)>> _pending = new();

        private string? _python;
        private Process? _process;
        private DateTime _failedAt = DateTime.MinValue;
        private long _nextId;

        public YtDlpWorker(ILogger<YtDlpWorker> logger) => _logger = logger;

        public string? Library { get; private set; }

        public static string Script => Path.Combine(AppContext.BaseDirectory, "ytdlp_worker.py");

        public void Use(string python, string library)
        {
            _python = python;
            Library = library;
            _failedAt = DateTime.MinValue;

            Stop();
        }

        /// <returns>yt-dlp's JSON, or null when the worker could not be asked.</returns>
        /// <exception cref="YtDlpFailure">yt-dlp itself refused the URL.</exception>
        public async Task<string?> TryFetchJsonAsync(string url, string? proxy, string? cookies, bool flat)
        {
            var process = await EnsureStartedAsync();

            if (process is null)
                return null;

            var id = Interlocked.Increment(ref _nextId);
            var reply = new TaskCompletionSource<(bool, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = reply;

            try
            {
                var request = JsonSerializer.Serialize(new { id, url, proxy, cookies, flat });

                lock (process)
                {
                    process.StandardInput.WriteLine(request);
                    process.StandardInput.Flush();
                }

                var (ok, payload) = await reply.Task.WaitAsync(LookupTimeout);

                return ok ? payload : throw new YtDlpFailure(payload);
            }
            catch (Exception ex) when (ex is not YtDlpFailure)
            {
                _logger.LogWarning(ex, "The yt-dlp worker did not answer for {Url}; running yt-dlp directly", url);
                return null;
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }

        private async Task<Process?> EnsureStartedAsync()
        {
            if (_process is { HasExited: false } running)
                return running;

            if (_python is null || Library is null || DateTime.UtcNow - _failedAt < RetryAfter)
                return null;

            await _starting.WaitAsync();

            try
            {
                if (_process is { HasExited: false } started)
                    return started;

                var process = Start(_python, Library);
                var ready = await process.StandardOutput.ReadLineAsync().WaitAsync(StartTimeout);

                if (ready?.StartsWith("0 ok ", StringComparison.Ordinal) != true)
                    throw new InvalidOperationException($"The worker answered '{ready}' instead of its version.");

                _logger.LogInformation("yt-dlp worker ready: yt-dlp {Version} from {Library}", ready[5..], Library);

                _ = ReadRepliesAsync(process);
                _process = process;

                return process;
            }
            catch (Exception ex)
            {
                _failedAt = DateTime.UtcNow;
                Stop();
                _logger.LogWarning(ex, "The yt-dlp worker would not start; lookups run yt-dlp directly for now");
                return null;
            }
            finally
            {
                _starting.Release();
            }
        }

        private Process Start(string python, string library)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = python,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };

            startInfo.ArgumentList.Add("-I");
            startInfo.ArgumentList.Add(Script);
            startInfo.ArgumentList.Add(library);
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

            var process = Process.Start(startInfo)!;

            // Undrained, a full stderr pipe would stall the worker mid-lookup.
            process.ErrorDataReceived += (_, line) =>
            {
                if (!string.IsNullOrWhiteSpace(line.Data))
                    _logger.LogDebug("yt-dlp worker: {Line}", line.Data);
            };
            process.BeginErrorReadLine();

            _process = process;

            return process;
        }

        private async Task ReadRepliesAsync(Process process)
        {
            try
            {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                {
                    var parts = line.Split(' ', 3);

                    if (parts.Length == 3 && long.TryParse(parts[0], out var id) && _pending.TryGetValue(id, out var reply))
                        reply.TrySetResult((parts[1] == "ok", parts[2]));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Reading from the yt-dlp worker failed");
            }

            if (_process == process)
                _logger.LogWarning("The yt-dlp worker stopped; it starts again on the next lookup");

            // ponytail: also fails lookups already sent to a successor - they fall back, nothing is lost.
            foreach (var reply in _pending.Values)
                reply.TrySetException(new IOException("The yt-dlp worker stopped."));
        }

        private void Stop()
        {
            var process = _process;
            _process = null;

            try
            {
                if (process is { HasExited: false })
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not stop the yt-dlp worker");
            }

            process?.Dispose();
        }

        public void Dispose() => Stop();
    }
}
