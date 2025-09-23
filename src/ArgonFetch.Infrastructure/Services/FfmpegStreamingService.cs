using ArgonFetch.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ArgonFetch.Infrastructure.Services
{
    public class FfmpegStreamingService : IFfmpegStreamingService
    {
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

            // Use HTTP input for better streaming support
            // Add user-agent header to avoid 403 errors from YouTube
            var arguments = $"-user_agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36\" " +
                           $"-i \"{videoUrl}\" " +
                           $"-user_agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36\" " +
                           $"-i \"{audioUrl}\" " +
                           "-map 0:v -map 1:a " +
                           "-c:v copy -c:a copy " +
                           "-movflags frag_keyframe+empty_moov+faststart " +
                           "-f mp4 " +
                           "-loglevel warning " +
                           "-max_muxing_queue_size 1024 " +
                           "pipe:1";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                CreateNoWindow = true
            };

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

            string arguments;
            if (isAudio)
            {
                // Convert any audio format to MP3
                arguments = $"-user_agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36\" " +
                           $"-i \"{sourceUrl}\" " +
                           "-vn " +  // Disable video
                           "-c:a mp3 " +  // Convert audio to MP3
                           "-b:a 192k " +  // Set bitrate to 192k
                           "-f mp3 " +  // Force MP3 format
                           "-loglevel warning " +
                           "pipe:1";
            }
            else
            {
                // Convert any video format to MP4 (with audio if present)
                arguments = $"-user_agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36\" " +
                           $"-i \"{sourceUrl}\" " +
                           "-c:v libx264 " +  // Use H.264 codec for video
                           "-preset ultrafast " +  // Fast encoding for streaming
                           "-crf 23 " +  // Quality setting (lower = better quality)
                           "-c:a aac " +  // Convert audio to AAC
                           "-b:a 128k " +  // Audio bitrate
                           "-movflags frag_keyframe+empty_moov+faststart " +
                           "-f mp4 " +
                           "-loglevel warning " +
                           "pipe:1";
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                CreateNoWindow = true
            };

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