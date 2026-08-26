using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ArgonFetch.Infrastructure.Services
{
    public class FfmpegStreamingService : IFfmpegStreamingService
    {
        private const string UserAgent = MediaHttpClientDefaults.UserAgent;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<FfmpegStreamingService> _logger;
        private readonly IToolPaths _toolPaths;

        public FfmpegStreamingService(IHttpClientFactory httpClientFactory, ILogger<FfmpegStreamingService> logger, IToolPaths toolPaths)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _toolPaths = toolPaths;
        }

        public async Task StreamCombinedMediaAsync(string videoUrl, string audioUrl, Stream outputStream, string? proxy = null, MediaTags? tags = null, CancellationToken cancellationToken = default)
        {
            var ffmpegPath = GetFfmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                throw new InvalidOperationException("FFmpeg not found in system PATH");
            }

            ValidateMediaUrl(videoUrl, nameof(videoUrl));
            ValidateMediaUrl(audioUrl, nameof(audioUrl));

            var processStartInfo = CreateProcessStartInfo(ffmpegPath);

            processStartInfo.ArgumentList.Add("-user_agent");
            processStartInfo.ArgumentList.Add(UserAgent);
            AddProxy(processStartInfo, proxy);
            processStartInfo.ArgumentList.Add("-i");
            processStartInfo.ArgumentList.Add(videoUrl);
            processStartInfo.ArgumentList.Add("-user_agent");
            processStartInfo.ArgumentList.Add(UserAgent);
            AddProxy(processStartInfo, proxy);
            processStartInfo.ArgumentList.Add("-i");
            processStartInfo.ArgumentList.Add(audioUrl);
            processStartInfo.ArgumentList.Add("-map");
            processStartInfo.ArgumentList.Add("0:v");
            processStartInfo.ArgumentList.Add("-map");
            processStartInfo.ArgumentList.Add("1:a");
            processStartInfo.ArgumentList.Add("-c:v");
            processStartInfo.ArgumentList.Add("copy");
            processStartInfo.ArgumentList.Add("-c:a");
            processStartInfo.ArgumentList.Add("copy");
            AddTags(processStartInfo, tags);
            processStartInfo.ArgumentList.Add("-movflags");
            processStartInfo.ArgumentList.Add("frag_keyframe+empty_moov+faststart");
            processStartInfo.ArgumentList.Add("-f");
            processStartInfo.ArgumentList.Add("mp4");
            processStartInfo.ArgumentList.Add("-loglevel");
            processStartInfo.ArgumentList.Add("warning");
            processStartInfo.ArgumentList.Add("-max_muxing_queue_size");
            processStartInfo.ArgumentList.Add("1024");
            processStartInfo.ArgumentList.Add("pipe:1");

            var arguments = DescribeArguments(processStartInfo);

            using var process = new Process { StartInfo = processStartInfo };

            var errorOutput = new List<string>();
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorOutput.Add(e.Data);
                    _logger.LogWarning("FFmpeg: {Error}", e.Data);
                }
            };

            _logger.LogInformation("Starting FFmpeg with arguments: {Arguments}", arguments);

            try
            {
                process.Start();
                process.BeginErrorReadLine();

                await process.StandardOutput.BaseStream.CopyToAsync(outputStream, 81920, cancellationToken);

                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    var errorMessage = string.Join("\n", errorOutput);
                    _logger.LogError("FFmpeg exited with code {ExitCode}. Errors: {Errors}", process.ExitCode, errorMessage);
                    throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}: {errorMessage}");
                }
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch { }
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during FFmpeg streaming");
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch { }
                }
                throw;
            }
        }

        public async Task ConvertAndStreamMediaAsync(string sourceUrl, Stream outputStream, bool isAudio, string? proxy = null, MediaTags? tags = null, CancellationToken cancellationToken = default)
        {
            var ffmpegPath = GetFfmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                throw new InvalidOperationException("FFmpeg not found in system PATH");
            }

            ValidateMediaUrl(sourceUrl, nameof(sourceUrl));

            var processStartInfo = CreateProcessStartInfo(ffmpegPath);

            processStartInfo.ArgumentList.Add("-user_agent");
            processStartInfo.ArgumentList.Add(UserAgent);
            AddProxy(processStartInfo, proxy);
            processStartInfo.ArgumentList.Add("-i");
            processStartInfo.ArgumentList.Add(sourceUrl);

            if (isAudio)
            {
                processStartInfo.ArgumentList.Add("-vn");        // Disable video
                // 2.3, not the default 2.4: many players and Explorer only read the older revision.
                processStartInfo.ArgumentList.Add("-id3v2_version");
                processStartInfo.ArgumentList.Add("3");
                processStartInfo.ArgumentList.Add("-c:a");
                processStartInfo.ArgumentList.Add("mp3");        // Convert audio to MP3
                processStartInfo.ArgumentList.Add("-b:a");
                processStartInfo.ArgumentList.Add($"{MediaFormats.Mp3BitrateKbps}k");
                processStartInfo.ArgumentList.Add("-f");
                processStartInfo.ArgumentList.Add("mp3");        // Force MP3 format
                AddTags(processStartInfo, tags);
            }
            else
            {
                processStartInfo.ArgumentList.Add("-c:v");
                processStartInfo.ArgumentList.Add("libx264");    // Use H.264 codec for video
                processStartInfo.ArgumentList.Add("-preset");
                processStartInfo.ArgumentList.Add("ultrafast");  // Fast encoding for streaming
                processStartInfo.ArgumentList.Add("-crf");
                processStartInfo.ArgumentList.Add("23");         // Quality setting (lower = better quality)
                processStartInfo.ArgumentList.Add("-c:a");
                processStartInfo.ArgumentList.Add("aac");        // Convert audio to AAC
                processStartInfo.ArgumentList.Add("-b:a");
                processStartInfo.ArgumentList.Add("128k");       // Audio bitrate
                processStartInfo.ArgumentList.Add("-movflags");
                processStartInfo.ArgumentList.Add("frag_keyframe+empty_moov+faststart");
                processStartInfo.ArgumentList.Add("-f");
                processStartInfo.ArgumentList.Add("mp4");
            }

            processStartInfo.ArgumentList.Add("-loglevel");
            processStartInfo.ArgumentList.Add("warning");
            processStartInfo.ArgumentList.Add("pipe:1");

            var arguments = DescribeArguments(processStartInfo);

            using var process = new Process { StartInfo = processStartInfo };

            var errorOutput = new List<string>();
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorOutput.Add(e.Data);
                    _logger.LogWarning("FFmpeg: {Error}", e.Data);
                }
            };

            _logger.LogInformation("Starting FFmpeg conversion with arguments: {Arguments}", arguments);

            try
            {
                process.Start();
                process.BeginErrorReadLine();

                await process.StandardOutput.BaseStream.CopyToAsync(outputStream, 81920, cancellationToken);

                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    var errorMessage = string.Join("\n", errorOutput);
                    _logger.LogError("FFmpeg exited with code {ExitCode}. Errors: {Errors}", process.ExitCode, errorMessage);
                    throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}: {errorMessage}");
                }
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch { }
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during FFmpeg conversion");
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch { }
                }
                throw;
            }
        }

        private static ProcessStartInfo CreateProcessStartInfo(string ffmpegPath)
        {
            return new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                CreateNoWindow = true
            };
        }

        private static void ValidateMediaUrl(string url, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("Media URL must not be empty.", parameterName);
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("Media URL must be an absolute http or https URL.", parameterName);
            }
        }

        private static void AddTags(ProcessStartInfo processStartInfo, MediaTags? tags)
        {
            if (tags is null || !tags.HasAny)
                return;

            if (!string.IsNullOrWhiteSpace(tags.Title))
            {
                processStartInfo.ArgumentList.Add("-metadata");
                processStartInfo.ArgumentList.Add($"title={tags.Title}");
            }

            if (!string.IsNullOrWhiteSpace(tags.Artist))
            {
                processStartInfo.ArgumentList.Add("-metadata");
                processStartInfo.ArgumentList.Add($"artist={tags.Artist}");
                processStartInfo.ArgumentList.Add("-metadata");
                processStartInfo.ArgumentList.Add($"album_artist={tags.Artist}");
            }
        }

        // Media URLs are signed for the requesting IP, so this must match the extraction.
        private static void AddProxy(ProcessStartInfo processStartInfo, string? proxy)
        {
            if (string.IsNullOrWhiteSpace(proxy))
                return;

            processStartInfo.ArgumentList.Add("-http_proxy");
            processStartInfo.ArgumentList.Add(proxy);
        }

        private static string DescribeArguments(ProcessStartInfo processStartInfo)
        {
            return string.Join(' ', processStartInfo.ArgumentList);
        }

        private string? GetFfmpegPath()
        {
            if (File.Exists(_toolPaths.FfmpegPath))
            {
                return _toolPaths.FfmpegPath;
            }

            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();

            foreach (var path in paths)
            {
                var ffmpegExe = Path.Combine(path, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
                if (File.Exists(ffmpegExe))
                {
                    return ffmpegExe;
                }
            }

            var commonPaths = new[]
            {
                "/usr/bin/ffmpeg",
                "/usr/local/bin/ffmpeg",
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe"
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }
    }
}