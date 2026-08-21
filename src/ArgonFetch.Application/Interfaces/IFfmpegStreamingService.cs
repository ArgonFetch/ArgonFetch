namespace ArgonFetch.Application.Interfaces
{
    public interface IFfmpegStreamingService
    {
        Task StreamCombinedMediaAsync(string videoUrl, string audioUrl, Stream outputStream, string? proxy = null, CancellationToken cancellationToken = default);
        Task ConvertAndStreamMediaAsync(string sourceUrl, Stream outputStream, bool isAudio, string? proxy = null, CancellationToken cancellationToken = default);
    }
}