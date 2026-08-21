using ArgonFetch.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ArgonFetch.Infrastructure.Services
{
    /// <summary>
    /// Keeps yt-dlp current for the lifetime of the container.
    /// <para>
    /// The image only downloads yt-dlp at build time, so a long-running deployment
    /// otherwise stays pinned to whatever version was current when the image was built.
    /// yt-dlp releases frequently because extractors break when the upstream sites change,
    /// so that version degrades until someone rebuilds.
    /// </para>
    /// <para>
    /// Update failures are logged and swallowed: a container with no network access, or one
    /// hitting a GitHub rate limit, must still start and serve requests with the binary it has.
    /// </para>
    /// </summary>
    public class YtDlpUpdateService : BackgroundService
    {
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(12);
        private static readonly TimeSpan UpdateTimeout = TimeSpan.FromMinutes(5);

        private readonly ILogger<YtDlpUpdateService> _logger;
        private readonly IMaintenanceState _maintenance;

        public YtDlpUpdateService(ILogger<YtDlpUpdateService> logger, IMaintenanceState maintenance)
        {
            _logger = logger;
            _maintenance = maintenance;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await TryUpdateAsync(stoppingToken);

                try
                {
                    await Task.Delay(UpdateInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Shutting down.
                    return;
                }
            }
        }

        private async Task TryUpdateAsync(CancellationToken cancellationToken)
        {
            // Held for the whole attempt, including the check that finds nothing to do: the
            // binary can be replaced at any point inside it, and a fetch that starts meanwhile
            // runs against a file that is being written.
            using var maintenance = _maintenance.Begin("Updating yt-dlp");

            try
            {
                // Bound the attempt so a hung download can't keep the timer from firing again.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(UpdateTimeout);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "yt-dlp",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("--update");

                using var process = new Process { StartInfo = startInfo };

                process.Start();

                var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

                await process.WaitForExitAsync(timeout.Token);

                var stdout = (await stdoutTask).Trim();
                var stderr = (await stderrTask).Trim();

                if (process.ExitCode == 0)
                {
                    _logger.LogInformation("yt-dlp update check: {Output}",
                        string.IsNullOrWhiteSpace(stdout) ? "already up to date" : stdout);
                }
                else
                {
                    // Most likely cause is the binary living somewhere the runtime user
                    // cannot write, which makes the in-place self-update impossible.
                    _logger.LogWarning(
                        "yt-dlp update failed with exit code {ExitCode}. Continuing with the installed version. {Error}",
                        process.ExitCode,
                        string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "yt-dlp update check could not run. Continuing with the installed version.");
            }
        }
    }
}
