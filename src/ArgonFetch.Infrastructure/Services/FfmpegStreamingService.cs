using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ArgonFetch.Infrastructure.Services
{
    public class FfmpegStreamingService : IFfmpegStreamingService
    {
        // Shared with the media HttpClient so the User-Agent that avoids upstream 403s
        // is defined in exactly one place.
        private const string UserAgent = MediaHttpClientDefaults.UserAgent;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<FfmpegStreamingService> _logger;

        public FfmpegStreamingService(IHttpClientFactory httpClientFactory, ILogger<FfmpegStreamingService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task StreamCombinedMediaAsync(string videoUrl, string audioUrl, Stream outputStream, CancellationToken cancellationToken = default)
        {
            var ffmpegPath = GetFfmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                throw new InvalidOperationException("FFmpeg not found in system PATH");
            }

            ValidateMediaUrl(videoUrl, nameof(videoUrl));
            ValidateMediaUrl(audioUrl, nameof(audioUrl));

            var processStartInfo = CreateProcessStartInfo(ffmpegPath);

            // Use HTTP input for better streaming support
            // Add user-agent header to avoid 403 errors from YouTube
            // Arguments are passed as separate tokens so a URL can never be parsed as an option.
            processStartInfo.ArgumentList.Add("-user_agent");
            processStartInfo.ArgumentList.Add(UserAgent);
            processStartInfo.ArgumentList.Add("-i");
            processStartInfo.ArgumentList.Add(videoUrl);
            processStartInfo.ArgumentList.Add("-user_agent");
            processStartInfo.ArgumentList.Add(UserAgent);
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

                // Stream the output to the client
                await process.StandardOutput.BaseStream.CopyToAsync(outputStream, 81920, cancellationToken);

                // Wait for process to complete
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
                // Client disconnected, kill the process
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

        public async Task ConvertAndStreamMediaAsync(string sourceUrl, Stream outputStream, bool isAudio, CancellationToken cancellationToken = default)
        {
            var ffmpegPath = GetFfmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                throw new InvalidOperationException("FFmpeg not found in system PATH");
            }

            ValidateMediaUrl(sourceUrl, nameof(sourceUrl));

            var processStartInfo = CreateProcessStartInfo(ffmpegPath);

            // Arguments are passed as separate tokens so a URL can never be parsed as an option.
            processStartInfo.ArgumentList.Add("-user_agent");
            processStartInfo.ArgumentList.Add(UserAgent);
            processStartInfo.ArgumentList.Add("-i");
            processStartInfo.ArgumentList.Add(sourceUrl);

            if (isAudio)
            {
                // Convert any audio format to MP3
                processStartInfo.ArgumentList.Add("-vn");        // Disable video
                processStartInfo.ArgumentList.Add("-c:a");
                processStartInfo.ArgumentList.Add("mp3");        // Convert audio to MP3
                processStartInfo.ArgumentList.Add("-b:a");
                processStartInfo.ArgumentList.Add("192k");       // Set bitrate to 192k
                processStartInfo.ArgumentList.Add("-f");
                processStartInfo.ArgumentList.Add("mp3");        // Force MP3 format
            }
            else
            {
                // Convert any video format to MP4 (with audio if present)
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

                // Stream the output to the client
                await process.StandardOutput.BaseStream.CopyToAsync(outputStream, 81920, cancellationToken);

                // Wait for process to complete
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
                // Client disconnected, kill the process
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

        /// <summary>
        /// FFmpeg treats a leading dash as an option and can read local paths as input,
        /// so only absolute http(s) URLs are accepted as media sources.
        /// </summary>
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

        /// <summary>
        /// Renders the argument list for logging only - it is never handed to the process.
        /// </summary>
        private static string DescribeArguments(ProcessStartInfo processStartInfo)
        {
            return string.Join(' ', processStartInfo.ArgumentList);
        }

        private string? GetFfmpegPath()
        {
            // Try to find ffmpeg in PATH
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();

            foreach (var path in paths)
            {
                var ffmpegExe = Path.Combine(path, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
                if (File.Exists(ffmpegExe))
                {
                    return ffmpegExe;
                }
            }

            // Check common installation locations
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