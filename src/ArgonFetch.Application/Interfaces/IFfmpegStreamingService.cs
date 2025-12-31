namespace ArgonFetch.Application.Interfaces
{
    public interface IFfmpegStreamingService
    {
        Task StreamCombinedMediaAsync(string videoUrl, string audioUrl, Stream outputStream, CancellationToken cancellationToken = default);
        Task ConvertAndStreamMediaAsync(string sourceUrl, Stream outputStream, bool isAudio, CancellationToken cancellationToken = default);
        Task<MemoryStream> GenerateCombinedMediaAsync(string videoUrl, string audioUrl, CancellationToken cancellationToken = default);
    }
}