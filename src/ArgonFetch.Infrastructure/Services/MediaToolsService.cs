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
        private readonly YtDlpWorker _worker;

        public MediaToolsService(
            ILogger<MediaToolsService> logger,
            IMaintenanceState maintenance,
            IHttpClientFactory httpClientFactory,
            IToolPaths paths,
            YtDlpWorker worker)
        {
            _logger = logger;
            _maintenance = maintenance;
            _httpClientFactory = httpClientFactory;
            _paths = paths;
            _worker = worker;
        }

        // yt-dlp as a Python library for the worker, one folder per version so a new one installs
        // beside the one in use. Deno comes with it; YouTube's download challenge needs a JS runtime.
        private async Task TryPrepareWorkerAsync(CancellationToken cancellationToken)
        {
            try
            {
                var python = await FindPythonAsync(cancellationToken);

                if (python is null)
                {
                    _logger.LogInformation("No Python 3.10+ found, so every lookup starts yt-dlp afresh");
                    return;
                }

                var root = Path.Combine(_paths.ToolsDirectory, "yt-dlp-python");
                Directory.CreateDirectory(root);

                var latest = await TryReadLatestYtDlpVersionAsync(cancellationToken);
                var library = latest is null ? null : Path.Combine(root, latest);

                if (library is not null && !Directory.Exists(library))
                    library = await TryInstallLibraryAsync(python, root, latest!, cancellationToken);

                // PyPI unreachable or the install failed: the newest one already here still works.
                library ??= Directory.GetDirectories(root)
                    .Where(directory => !directory.EndsWith(".partial", StringComparison.Ordinal))
                    .OrderByDescending(directory => Version.TryParse(Path.GetFileName(directory), out var version) ? version : new Version())
                    .FirstOrDefault();

                if (library is null || library == _worker.Library)
                    return;

                _worker.Use(python, library);
                _logger.LogInformation("Lookups go through the yt-dlp worker, using {Library}", library);

                foreach (var old in Directory.GetDirectories(root).Where(directory => directory != library))
                {
                    try { Directory.Delete(old, recursive: true); }
                    catch (Exception ex) { _logger.LogDebug(ex, "Could not remove {Directory}", old); }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not prepare the yt-dlp worker; lookups run yt-dlp directly");
            }
        }

        // Unpinned, so pip settles on the newest release this Python can run; the folder is named
        // after whatever that turns out to be.
        private async Task<string?> TryInstallLibraryAsync(string python, string root, string latest, CancellationToken cancellationToken)
        {
            var partial = Path.Combine(root, "next.partial");

            if (Directory.Exists(partial))
                Directory.Delete(partial, recursive: true);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(UpdateTimeout);

            var (exitCode, _, stderr) = await RunAsync(python, timeout.Token,
                "-m", "pip", "install", "--disable-pip-version-check", "--no-input", "--quiet",
                "--target", partial, "yt-dlp[default,deno]");

            if (exitCode != 0)
            {
                _logger.LogWarning("Could not install yt-dlp for the worker: {Error}", stderr);
                return null;
            }

            var installed = Directory.GetDirectories(partial, "yt_dlp-*.dist-info")
                .Select(Path.GetFileName)
                .Select(name => name!["yt_dlp-".Length..^".dist-info".Length])
                .FirstOrDefault() ?? latest;

            if (installed != latest)
            {
                // ponytail: re-downloads every check while Python is too old - a newer image ends it.
                _logger.LogWarning(
                    "yt-dlp {Latest} needs a newer Python than {Python} offers; the worker stays on {Installed}",
                    latest, python, installed);
            }

            var library = Path.Combine(root, installed);

            if (Directory.Exists(library))
                Directory.Delete(partial, recursive: true);
            else
                Directory.Move(partial, library);

            return library;
        }

        private static async Task<string?> FindPythonAsync(CancellationToken cancellationToken)
        {
            foreach (var candidate in new[] { "python3", "python" })
            {
                try
                {
                    var (exitCode, stdout, _) = await RunAsync(candidate, cancellationToken,
                        "-c", "import sys; print(sys.version_info >= (3, 10))");

                    if (exitCode == 0 && stdout == "True")
                        return candidate;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Not installed under this name.
                }
            }

            return null;
        }

        private async Task<string?> TryReadLatestYtDlpVersionAsync(CancellationToken cancellationToken)
        {
            try
            {
                await using var body = await OpenDownloadAsync("https://pypi.org/pypi/yt-dlp/json", cancellationToken);
                using var document = await System.Text.Json.JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

                return document.RootElement.GetProperty("info").GetProperty("version").GetString();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not ask PyPI for the latest yt-dlp");
                return null;
            }
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

                // yt-dlp is unusable while it is written, whether first install or --update.
                using (_maintenance.Begin(File.Exists(_paths.YtDlpPath) ? "Updating yt-dlp" : "Downloading yt-dlp"))
                {
                    if (!File.Exists(_paths.YtDlpPath))
                    {
                        await TryDownloadYtDlpAsync(stoppingToken);
                    }

                    await TryUpdateYtDlpAsync(stoppingToken);

                    await LogVersionsAsync(stoppingToken);
                }

                // Outside maintenance: until the worker is ready, lookups run the binary above.
                await TryPrepareWorkerAsync(stoppingToken);

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
