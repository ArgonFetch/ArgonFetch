namespace ArgonFetch.Application.Services
{
    public interface IDataPaths
    {
        /// <summary>Directory the app's own small state files live in.</summary>
        string DataDirectory { get; }

        /// <summary>Full path of the request-counter file, whether or not it exists yet.</summary>
        string RequestCounterPath { get; }
    }

    /// <summary>
    /// Where the little state this app keeps is written. There is no database behind any of
    /// it - the only thing outliving a request is a counter - so <c>DATA_PATH</c> points at a
    /// directory that has to be writable by the runtime user, and mounting a volume there is
    /// what makes the count survive a restart.
    /// </summary>
    public class DataPaths : IDataPaths
    {
        public DataPaths(string? dataDirectory)
        {
            DataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "data")
                : dataDirectory;

            RequestCounterPath = Path.Combine(DataDirectory, "request-counter.json");
        }

        public string DataDirectory { get; }

        public string RequestCounterPath { get; }
    }
}
