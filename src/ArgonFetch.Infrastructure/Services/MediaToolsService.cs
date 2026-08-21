using ArgonFetch.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpCompress.Compressors.Xz;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace ArgonFetch.Infrastructure.Services
{
    /// <summary>
    /// Owns the media tooling: fetches yt-dlp and FFmpeg when the app boots and keeps yt-dlp
    /// current afterwards.
    /// <para>
    /// Baking them into the image pinned every deployment to whatever was current on build day,
    /// and yt-dlp breaks whenever the sites it extracts from change. Fetching at boot means a
    /// restart is enough to recover, and the image ships without media tooling at all - which is
    /// also what lets it run on a minimal base with no package manager involved.
    /// </para>
    /// <para>
    /// The app reports itself as under maintenance while this runs, so a fetch arriving before
    /// the binaries exist is refused with a reason instead of failing on a missing file.
    /// </para>
    /// </summary>
    public class MediaToolsService : BackgroundService
    {
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(12);
        private static readonly TimeSpan UpdateTimeout = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

        private const string YtDlpBaseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";
        private const string FfmpegBaseUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/";

        private readonly ILogger<MediaToolsService> _logger;
        private readonly IMaintenanceState _maintenance;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IToolPaths _paths;

        public MediaToolsService(
            ILogger<MediaToolsService> logger,
            IMaintenanceState maintenance,
            IHttpClientFactory httpClientFactory,
            IToolPaths paths)
        {
            _logger = logger;
            _maintenance = maintenance;
            _httpClientFactory = httpClientFactory;
            _paths = paths;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Directory.CreateDirectory(_paths.ToolsDirectory);

                if (!File.Exists(_paths.FfmpegPath))
                {
                    // FFmpeg is fetched once and then left alone: it is a stable dependency,
                    // unlike yt-dlp, and the archive is large enough that re-fetching it on a
                    // timer would cost far more than it is worth.
                    using (_maintenance.Begin("Downloading FFmpeg"))
                    {
                        await TryDownloadFfmpegAsync(stoppingToken);
                    }
                }

                // One window covers both steps: whether yt-dlp is being written for the first
                // time or replaced by --update, it is unusable while it happens.
                using (_maintenance.Begin(File.Exists(_paths.YtDlpPath) ? "Updating yt-dlp" : "Downloading yt-dlp"))
                {
                    if (!File.Exists(_paths.YtDlpPath))
                    {
                        await TryDownloadYtDlpAsync(stoppingToken);
                    }

                    await TryUpdateYtDlpAsync(stoppingToken);

                    // Logged rather than served: which extractor build is running is useful
                    // when debugging, and nothing a caller needs to be told.
                    await LogVersionsAsync(stoppingToken);
                }

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

        /// <summary>
        /// Fetches the standalone yt-dlp build for the current platform. The generic "yt-dlp"
        /// asset needs a Python interpreter beside it; the per-platform ones do not, which is
        /// what keeps Python out of the image.
        /// </summary>
        private async Task TryDownloadYtDlpAsync(CancellationToken cancellationToken)
        {
            var assetName = OperatingSystem.IsWindows()
                ? "yt-dlp.exe"
                : OperatingSystem.IsMacOS()
                    ? "yt-dlp_macos"
                    : RuntimeInformation.OSArchitecture switch
                    {
                        Architecture.Arm64 => "yt-dlp_linux_aarch64",
                        Architecture.Arm => "yt-dlp_linux_armv7l",
                        _ => "yt-dlp_linux",
                    };

            try
            {
                _logger.LogInformation("Downloading {Asset} to {Path}", assetName, _paths.YtDlpPath);

                using var timeout = Bounded(cancellationToken);
                await using var download = await OpenDownloadAsync(YtDlpBaseUrl + assetName, timeout.Token);

                await WriteExecutableAsync(_paths.YtDlpPath, download, timeout.Token);

                _logger.LogInformation("yt-dlp downloaded to {Path}", _paths.YtDlpPath);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Retried on the next cycle. Failing to start over it would take the whole app
                // down for what is usually a transient network problem.
                _logger.LogError(ex, "Could not download yt-dlp. Fetching will be retried later.");
            }
        }

        /// <summary>
        /// Fetches a static FFmpeg build and lifts the single binary out of the archive. The GPL
        /// build is the one carrying libx264, which the conversion path needs.
        /// </summary>
        private async Task TryDownloadFfmpegAsync(CancellationToken cancellationToken)
        {
            var isWindows = OperatingSystem.IsWindows();

            var assetName = isWindows
                ? "ffmpeg-master-latest-win64-gpl.zip"
                : RuntimeInformation.OSArchitecture == Architecture.Arm64
                    ? "ffmpeg-master-latest-linuxarm64-gpl.tar.xz"
                    : "ffmpeg-master-latest-linux64-gpl.tar.xz";

            try
            {
                _logger.LogInformation("Downloading {Asset} to {Path}", assetName, _paths.FfmpegPath);

                using var timeout = Bounded(cancellationToken);

                // Both archive formats need to seek, which a response stream cannot do, so the
                // download lands on disk first. A file rather than memory: the archive runs to
                // tens of megabytes and a container is usually the tighter on RAM of the two.
                var archivePath = Path.Combine(_paths.ToolsDirectory, assetName);

                try
                {
                    await using (var download = await OpenDownloadAsync(FfmpegBaseUrl + assetName, timeout.Token))
                    await using (var file = File.Create(archivePath))
                    {
                        await download.CopyToAsync(file, timeout.Token);
                    }

                    await using var archive = File.OpenRead(archivePath);

                    if (isWindows)
                    {
                        await ExtractFromZipAsync(archive, timeout.Token);
                    }
                    else
                    {
                        await ExtractFromTarXzAsync(archive, timeout.Token);
                    }
                }
                finally
                {
                    File.Delete(archivePath);
                }

                _logger.LogInformation("FFmpeg extracted to {Path}", _paths.FfmpegPath);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Conversion is the only thing that needs FFmpeg, and it is now the exception
                // rather than the rule, so the app stays up and retries on the next cycle.
                _logger.LogError(ex, "Could not download FFmpeg. Conversion will not work until it succeeds.");
            }
        }

        private async Task ExtractFromTarXzAsync(Stream archive, CancellationToken cancellationToken)
        {
            await using var xz = new XZStream(archive);
            await using var tar = new TarReader(xz);

            while (await tar.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
            {
                if (entry.DataStream == null || !entry.Name.EndsWith("/bin/ffmpeg", StringComparison.Ordinal))
                    continue;

                await WriteExecutableAsync(_paths.FfmpegPath, entry.DataStream, cancellationToken);
                return;
            }

            throw new InvalidOperationException("The FFmpeg archive did not contain a bin/ffmpeg entry.");
        }

        private async Task ExtractFromZipAsync(Stream archive, CancellationToken cancellationToken)
        {
            using var zip = new ZipArchive(archive, ZipArchiveMode.Read);

            var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("The FFmpeg archive did not contain a bin/ffmpeg.exe entry.");

            await using var content = entry.Open();
            await WriteExecutableAsync(_paths.FfmpegPath, content, cancellationToken);
        }

        /// <summary>
        /// Writes to a temporary name and moves it into place, so a download that dies halfway
        /// cannot leave a truncated binary that looks installed.
        /// </summary>
        private static async Task WriteExecutableAsync(string path, Stream content, CancellationToken cancellationToken)
        {
            var partialPath = path + ".partial";

            await using (var file = File.Create(partialPath))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    partialPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            File.Move(partialPath, path, overwrite: true);
        }

        private async Task<Stream> OpenDownloadAsync(string url, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient();

            var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }

        private static CancellationTokenSource Bounded(CancellationToken cancellationToken)
        {
            var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DownloadTimeout);

            return timeout;
        }

        private async Task TryUpdateYtDlpAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_paths.YtDlpPath))
                return;

            try
            {
                // Bound the attempt so a hung download can't keep the timer from firing again.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(UpdateTimeout);

                var (exitCode, stdout, stderr) = await RunAsync(_paths.YtDlpPath, timeout.Token, "--update");

                if (exitCode == 0)
                {
                    _logger.LogInformation("yt-dlp update check: {Output}",
                        string.IsNullOrWhiteSpace(stdout) ? "already up to date" : stdout);
                }
                else
                {
                    _logger.LogWarning(
                        "yt-dlp update failed with exit code {ExitCode}. Continuing with the installed version. {Error}",
                        exitCode,
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

        /// <summary>
        /// Records which tool builds are in use. Kept to the log rather than any endpoint:
        /// naming exact versions to callers only helps someone matching them to known exploits.
        /// </summary>
        private async Task LogVersionsAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Media tools ready: yt-dlp {YtDlpVersion}, FFmpeg {FfmpegVersion}",
                await ReadVersionAsync(_paths.YtDlpPath, cancellationToken, "--version") ?? "missing",
                await ReadVersionAsync(_paths.FfmpegPath, cancellationToken, "-version") ?? "missing");
        }

        private async Task<string?> ReadVersionAsync(
            string toolPath,
            CancellationToken cancellationToken,
            params string[] arguments)
        {
            if (!File.Exists(toolPath))
                return null;

            try
            {
                var (exitCode, stdout, _) = await RunAsync(toolPath, cancellationToken, arguments);

                if (exitCode != 0)
                    return null;

                // ffmpeg answers with a whole banner; only its first line carries the version.
                var firstLine = stdout.Split('\n').FirstOrDefault()?.Trim();

                return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read the version of {Tool}.", toolPath);
                return null;
            }
        }

        private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
            string toolPath,
            CancellationToken cancellationToken,
            params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };

            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            return (process.ExitCode, (await stdoutTask).Trim(), (await stderrTask).Trim());
        }
    }
}
