using ArgonFetch.Application.Services;

namespace ArgonFetch.Application.Interfaces
{
    public interface IFfmpegStreamingService
    {
        Task StreamCombinedMediaAsync(string videoUrl, string audioUrl, Stream outputStream, string? proxy = null, MediaTags? tags = null, CancellationToken cancellationToken = default);
        Task ConvertAndStreamMediaAsync(string sourceUrl, Stream outputStream, bool isAudio, string? proxy = null, MediaTags? tags = null, CancellationToken cancellationToken = default);
    }
}