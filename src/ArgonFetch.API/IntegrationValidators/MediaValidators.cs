using System.Diagnostics;

namespace ArgonFetch.API.IntegrationValidators
{
    public static class MediaValidators
    {
        /// <summary>
        /// Retrieves the version of the yt-dlp executable.
        /// </summary>
        /// <returns>A string containing the version of yt-dlp, or null if an error occurs.</returns>
        public static async Task<string> GetYtDlpVersionAsync()
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "yt-dlp",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = processStartInfo })
                {
                    process.Start();
                    string output = await process.StandardOutput.ReadToEndAsync();
                    process.WaitForExit();

                    return output.Trim();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Retrieves the version of the FFmpeg executable.
        /// </summary>
        /// <returns>A string containing the version of FFmpeg, or null if an error occurs.</returns>
        public static async Task<string> GetFfmpegVersionAsync()
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = processStartInfo })
                {
                    process.Start();
                    string output = await process.StandardOutput.ReadLineAsync();
                    process.WaitForExit();

                    return output?.Split(' ')[2].Trim();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
