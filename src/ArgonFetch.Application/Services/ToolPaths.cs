namespace ArgonFetch.Application.Services
{
    public interface IToolPaths
    {
        /// <summary>Directory the fetched tooling lives in.</summary>
        string ToolsDirectory { get; }

        /// <summary>Full path of the yt-dlp binary, whether or not it exists yet.</summary>
        string YtDlpPath { get; }

        /// <summary>Full path of the FFmpeg binary, whether or not it exists yet.</summary>
        string FfmpegPath { get; }
    }

    /// <summary>
    /// Where the media tools are kept. They are fetched at boot rather than baked into the
    /// image, so the location has to be writable by the runtime user - <c>TOOLS_PATH</c> points
    /// at it, and mounting a volume there turns the download into a one-off rather than a cost
    /// paid on every restart.
    /// </summary>
    public class ToolPaths : IToolPaths
    {
        public ToolPaths(string? toolsDirectory)
        {
            ToolsDirectory = string.IsNullOrWhiteSpace(toolsDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "tools")
                : toolsDirectory;

            var executableSuffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;

            YtDlpPath = Path.Combine(ToolsDirectory, "yt-dlp" + executableSuffix);
            FfmpegPath = Path.Combine(ToolsDirectory, "ffmpeg" + executableSuffix);
        }

        public string ToolsDirectory { get; }

        public string YtDlpPath { get; }

        public string FfmpegPath { get; }
    }
}
