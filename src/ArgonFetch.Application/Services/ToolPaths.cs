namespace ArgonFetch.Application.Services
{
    public interface IToolPaths
    {
        string ToolsDirectory { get; }

        string YtDlpPath { get; }

        string FfmpegPath { get; }

        string? CookiesPath { get; }
    }

    public class ToolPaths : IToolPaths
    {
        public ToolPaths(string? toolsDirectory, string? cookiesPath = null)
        {
            CookiesPath = !string.IsNullOrWhiteSpace(cookiesPath) && File.Exists(cookiesPath)
                ? cookiesPath
                : null;

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

        public string? CookiesPath { get; }
    }
}
