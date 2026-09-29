using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Services;
using Mediator;
using Microsoft.Extensions.Logging;
using YoutubeDLSharp.Metadata;
using MediaTags = ArgonFetch.Application.Services.MediaTags;

namespace ArgonFetch.Application.Queries
{
    /// <summary>
    /// Resolves a link into streams that can be played rather than files to be saved.
    /// </summary>
    public class GetPlaybackQuery : IRequest<PlaybackInformationDto>
    {
        public GetPlaybackQuery(string url)
        {
            Query = url;
        }

        public string Query { get; set; }
    }

    public class GetPlaybackQueryHandler : IRequestHandler<GetPlaybackQuery, PlaybackInformationDto>
    {
        private readonly IMediaMetadataFetcher _metadataFetcher;
        private readonly IMediaUrlCacheService _cacheService;
        private readonly ILogger<GetPlaybackQueryHandler> _logger;

        public GetPlaybackQueryHandler(
            IMediaMetadataFetcher metadataFetcher,
            IMediaUrlCacheService cacheService,
            ILogger<GetPlaybackQueryHandler> logger)
        {
            _metadataFetcher = metadataFetcher;
            _cacheService = cacheService;
            _logger = logger;
        }

        public async ValueTask<PlaybackInformationDto> Handle(GetPlaybackQuery request, CancellationToken cancellationToken)
        {
            var fetched = await _metadataFetcher.FetchAsync(request.Query);
            var data = fetched.Data;

            if (data.ResultType != MetadataType.Video)
                throw new NotSupportedException("Only a single piece of media can be played; this link is a collection.");

            var formats = data.Formats ?? [];
            var tags = new MediaTags(data.Title, data.Uploader ?? data.Channel);

            var video = PlaybackTrackPicker.PickVideo(Sources(formats, VideoOnly))
                .Select(source => Track(source, isAudio: false, tags, fetched.Proxy))
                .ToList();

            var audio = PlaybackTrackPicker.PickAudio(Sources(formats, AudioOnly))
                .Select(source => Track(source, isAudio: true, tags, fetched.Proxy))
                .ToList();

            var muxed = PlaybackTrackPicker.PickVideo(Sources(formats, PreMuxed))
                .Select(source => Track(source, isAudio: false, tags, fetched.Proxy))
                .ToList();

            var subtitles = Subtitles(data, tags, fetched.Proxy);

            _logger.LogInformation(
                "Playback for {Url}: {Video} video, {Audio} audio, {Muxed} muxed tracks",
                request.Query, video.Count, audio.Count, muxed.Count);

            return new PlaybackInformationDto
            {
                RequestedUrl = request.Query,
                Title = data.Title ?? string.Empty,
                Author = tags.Artist ?? string.Empty,
                CoverUrl = Thumbnail(data),
                DurationSeconds = data.Duration,
                Video = video,
                Audio = audio,
                Muxed = muxed,
                Subtitles = subtitles
            };
        }

        private PlaybackTrackDto Track(PlaybackSource source, bool isAudio, MediaTags tags, string? proxy)
        {
            var mimeType = MediaFormats.MimeTypeFor(source.Extension, isAudio)
                ?? (isAudio ? "audio/mp4" : "video/mp4");

            var key = _cacheService.CacheSingleUrl(source.Url, isAudio, mimeType, proxy, tags);

            return new PlaybackTrackDto
            {
                Key = key,
                Path = $"/api/Stream/Media/{key}",
                Label = PlaybackTrackPicker.Label(source, isAudio),
                Description = source.Description,
                ContentType = PlaybackTrackPicker.ContentType(mimeType, source.VideoCodec, source.AudioCodec),
                MimeType = mimeType,
                Codec = PlaybackTrackPicker.Codec(source.VideoCodec, source.AudioCodec),
                Width = source.Width,
                Height = source.Height,
                Fps = source.Fps,
                Bitrate = source.Bitrate,
                FileSizeBytes = source.FileSizeBytes
            };
        }

        /// <summary>
        /// The subtitle tracks, written by people first and by the source's speech recognition
        /// after, one per language. They are proxied rather than linked: the source signs those
        /// URLs for its own player and answers anyone else with an empty body.
        /// </summary>
        private List<PlaybackSubtitleDto> Subtitles(VideoData data, MediaTags tags, string? proxy)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tracks = new List<PlaybackSubtitleDto>();

            void Collect(Dictionary<string, SubtitleData[]>? source, bool automatic)
            {
                foreach (var (language, variants) in source ?? [])
                {
                    if (string.IsNullOrWhiteSpace(language) || !seen.Add(language))
                        continue;

                    // Whatever the source offers, taken as the format a browser can read without
                    // help; the stream endpoint converts anything else on the way out.
                    var chosen = variants?.FirstOrDefault(v => Matches(v.Ext, "vtt"))
                        ?? variants?.FirstOrDefault(v => Matches(v.Ext, "srv3") || Matches(v.Ext, "ttml"))
                        ?? variants?.FirstOrDefault();

                    if (chosen?.Url is null)
                        continue;

                    // YouTube lists its speech recognition once per language it will machine
                    // translate that recognition into - upwards of a hundred and fifty entries,
                    // all of them the same track put through a translator. Only the one it was
                    // actually recognised in is worth offering, and that is the one asking for
                    // no translation.
                    if (automatic && Translated(chosen.Url))
                        continue;

                    var key = _cacheService.CacheSingleUrl(chosen.Url, isAudio: false, "text/vtt", proxy, tags);

                    tracks.Add(new PlaybackSubtitleDto
                    {
                        Key = key,
                        Path = $"/api/Stream/Subtitle/{key}",
                        Language = language,
                        Name = string.IsNullOrWhiteSpace(chosen.Name) ? language : chosen.Name,
                        Automatic = automatic
                    });
                }
            }

            Collect(data.Subtitles, automatic: false);
            Collect(data.AutomaticCaptions, automatic: true);

            return tracks;
        }

        private static bool Matches(string? extension, string wanted) =>
            extension?.Trim('.').Equals(wanted, StringComparison.OrdinalIgnoreCase) == true;

        /// <summary>A caption URL that asks the source to translate on the way out.</summary>
        private static bool Translated(string url) =>
            url.Contains("tlang=", StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<PlaybackSource> Sources(FormatData[] formats, Func<FormatData, bool> keep) =>
            formats.Where(format => Playable(format) && keep(format)).Select(ToSource);

        // Manifests and storyboards are not streams a media element can open.
        private static bool Playable(FormatData format) =>
            !string.IsNullOrEmpty(format.Url) &&
            format.Protocol?.Contains("mhtml") != true &&
            format.Protocol?.Contains("m3u8") != true &&
            format.Protocol?.Contains("dash") != true;

        private static bool VideoOnly(FormatData format) =>
            Has(format.VideoCodec) && !Has(format.AudioCodec);

        private static bool AudioOnly(FormatData format) =>
            Has(format.AudioCodec) && !Has(format.VideoCodec);

        private static bool PreMuxed(FormatData format) =>
            Has(format.VideoCodec) && Has(format.AudioCodec);

        private static bool Has(string? codec) =>
            !string.IsNullOrEmpty(codec) && codec != "none";

        private static PlaybackSource ToSource(FormatData format) => new(
            format.Url,
            format.Format,
            format.Extension,
            format.VideoCodec,
            format.AudioCodec,
            format.Width,
            format.Height,
            format.FrameRate,
            format.Bitrate ?? format.AudioBitrate,
            (long?)(format.FileSize ?? format.ApproximateFileSize));

        private static string? Thumbnail(VideoData data)
        {
            var largest = data.Thumbnails?
                .Where(thumbnail => !string.IsNullOrWhiteSpace(thumbnail.Url))
                .OrderByDescending(thumbnail => (long)(thumbnail.Width ?? 0) * (thumbnail.Height ?? 0))
                .FirstOrDefault();

            return largest?.Url ?? (string.IsNullOrWhiteSpace(data.Thumbnail) ? null : data.Thumbnail);
        }
    }
}
