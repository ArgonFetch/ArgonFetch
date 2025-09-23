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
    }
}