using ArgonFetch.Application.Services;

namespace ArgonFetch.Application.Interfaces
{
    public interface IAcceleratedDownloadService
    {
        /// <param name="range">
        /// Window of the resource to write, or null for all of it. Set when a client is
        /// seeking or resuming, in which case only these bytes may reach the output.
        /// </param>
        Task StreamWithAccelerationAsync(
            string url,
            Stream outputStream,
            IProgress<double>? progress = null,
            string? proxy = null,
            ByteRange? range = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Length the upstream reports for the resource, or null when it does not report one.
        /// Callers use it to declare Content-Length on a pass-through response.
        /// </summary>
        Task<long?> GetContentLengthAsync(
            string url,
            string? proxy = null,
            CancellationToken cancellationToken = default);
    }
}