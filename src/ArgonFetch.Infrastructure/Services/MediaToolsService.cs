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

                    await LogVersionsAsync(stoppingToken);
                }

                try
                {
                    await Task.Delay(UpdateInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

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
                _logger.LogError(ex, "Could not download yt-dlp. Fetching will be retried later.");
            }
        }

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
