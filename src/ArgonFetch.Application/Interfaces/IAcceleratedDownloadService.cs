namespace ArgonFetch.Application.Interfaces
{
    public interface IAcceleratedDownloadService
    {
        Task<Stream> DownloadWithAccelerationAsync(
            string url,
            CancellationToken cancellationToken = default);

        Task StreamWithAccelerationAsync(
            string url,
            Stream outputStream,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Length the upstream reports for the resource, or null when it does not report one.
        /// Callers use it to declare Content-Length on a pass-through response.
        /// </summary>
        Task<long?> GetContentLengthAsync(
            string url,
            CancellationToken cancellationToken = default);
    }
}