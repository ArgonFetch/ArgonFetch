using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ArgonFetch.Application.Services
{
    /// <summary>
    /// A client wrapper for the yt-dlp command-line tool that provides methods to interact with video platforms.
    /// </summary>
    public class YTDLPClient
    {
        private readonly string _ytdlpPath;

        /// <summary>
        /// Initializes a new instance of the YTDLPClient class and determines the appropriate yt-dlp executable path.
        /// </summary>
        public YTDLPClient()
        {
            _ytdlpPath = GetYtdlpPath();
        }

        /// <summary>
        /// Gets the appropriate path for the yt-dlp executable based on the operating system.
        /// </summary>
        /// <returns>The path to the yt-dlp executable ("yt-dlp.exe" for Windows, "yt-dlp" for other platforms).</returns>
        private static string GetYtdlpPath()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "yt-dlp.exe"
                : "yt-dlp";
        }

        /// <summary>
        /// Verifies if the yt-dlp executable is available and accessible in the system.
        /// </summary>
        /// <returns>True if yt-dlp is available and responds to version check, false otherwise.</returns>
        public async Task<bool> VerifyYtdlpExistsAsync()
        {
            try
            {
                var result = await ExecuteCommandAsync("--version");
                return !string.IsNullOrEmpty(result);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Retrieves detailed information about a video from the provided URL with optional parameters.
        /// </summary>
        /// <param name="url">The URL of the video to get information for.</param>
        /// <param name="optionalParams">Optional parameters to pass to yt-dlp.</param>
        /// <returns>A JSON string containing the video information.</returns>
        /// <exception cref="Exception">Thrown when yt-dlp fails to retrieve video information.</exception>
        public async Task<string> GetVideoInfoAsync(string url, List<string> optionalParams = null)
        {
            optionalParams ??= new List<string>();
            if (!optionalParams.Contains("-J"))
            {
                optionalParams.Insert(0, "-J");
            }
            optionalParams.Add($"\"{url}\"");
            var args = string.Join(" ", optionalParams);
            var result = await ExecuteCommandAsync(args);
            return result;
        }

        /// <summary>
        /// Executes a yt-dlp command with the specified arguments.
        /// </summary>
        /// <param name="arguments">Command line arguments to pass to yt-dlp.</param>
        /// <returns>The command output as a string.</returns>
        /// <exception cref="Exception">Thrown when the command execution fails.</exception>
        private async Task<string> ExecuteCommandAsync(string arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ytdlpPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            var output = new List<string>();
            var error = new List<string>();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null) output.Add(e.Data);
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null) error.Add(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception($"yt-dlp error: {string.Join("\n", error)}");
            }

            return string.Join("\n", output);
        }
    }
}
