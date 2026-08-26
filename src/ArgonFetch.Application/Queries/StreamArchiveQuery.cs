using System.IO.Compression;
using System.Text;
using ArgonFetch.Application.Dtos;
using ArgonFetch.Application.Enums;
using ArgonFetch.Application.Interfaces;
using ArgonFetch.Application.Services;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Application.Queries
{
    public class StreamArchiveQuery : IRequest<StreamResult>
    {
        public StreamArchiveQuery(string url, string? jobId, HttpResponse response, CancellationToken cancellationToken)
        {
            Url = url;
            JobId = jobId;
            Response = response;
            CancellationToken = cancellationToken;
        }

        public string Url { get; }

        public string? JobId { get; }
        public HttpResponse Response { get; }
        public CancellationToken CancellationToken { get; }
    }

    public class StreamArchiveQueryHandler : IRequestHandler<StreamArchiveQuery, StreamResult>
    {
        internal const int MaxTracks = 100;

        private const int ResolveConcurrency = 4;

        private readonly IMediator _mediator;
        private readonly IMediaUrlCacheService _cacheService;
        private readonly IAcceleratedDownloadService _downloadService;
        private readonly IArchiveProgressTracker _progress;
        private readonly ILogger<StreamArchiveQueryHandler> _logger;

        public StreamArchiveQueryHandler(
            IMediator mediator,
            IMediaUrlCacheService cacheService,
            IAcceleratedDownloadService downloadService,
            IArchiveProgressTracker progress,
            ILogger<StreamArchiveQueryHandler> logger)
        {
            _mediator = mediator;
            _cacheService = cacheService;
            _downloadService = downloadService;
            _progress = progress;
            _logger = logger;
        }

        public async ValueTask<StreamResult> Handle(StreamArchiveQuery request, CancellationToken cancellationToken)
        {
            ResourceInformationDto listing;

            try
            {
                listing = await _mediator.Send(new GetMediaQuery(request.Url), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read {Url} as a collection", request.Url);
                FailBeforeStarting(request.JobId);
                return StreamResult.NotFound("That link could not be read as a playlist or album.");
            }

            if (listing.Type != MediaType.PlayList)
            {
                FailBeforeStarting(request.JobId);
                return StreamResult.BadRequest("That link is a single track, not a collection.");
            }

            var entries = listing.MediaItems?.ToList() ?? [];
            var wanted = entries.Take(MaxTracks).ToList();
            var omitted = entries.Count - wanted.Count;

            if (wanted.Count == 0)
            {
                FailBeforeStarting(request.JobId);
                return StreamResult.NotFound("That collection lists no tracks.");
            }

            var archiveName = MediaFileName.For(new MediaTags(listing.Title, listing.Author), ".zip", "playlist");

            request.Response.ContentType = "application/zip";
            request.Response.Headers.ContentDisposition =
                MediaFileName.ContentDisposition(new MediaTags(listing.Title, listing.Author), ".zip", "playlist");

            request.Response.Headers.Append("Cache-Control", "no-store");

            _logger.LogInformation(
                "Archiving {Count} of {Total} tracks from {Url} as {Name}",
                wanted.Count, entries.Count, request.Url, archiveName);

            var failures = new List<string>();
            var jobId = request.JobId;

            if (jobId is not null)
                _progress.Start(jobId, wanted.Count);

            try
            {
                // Async throughout: the central directory is written on dispose, and Kestrel
                // refuses a synchronous write to a response body.
                // leaveOpen: Kestrel owns that body and closes it itself.
                await using var archive = await ZipArchive.CreateAsync(
                    request.Response.Body, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null);

                using var gate = new SemaphoreSlim(ResolveConcurrency);

                var resolving = wanted
                    .Select(entry => ResolveAsync(entry, gate, cancellationToken))
                    .ToList();

                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (var index = 0; index < resolving.Count; index++)
                {
                    if (jobId is not null)
                        _progress.Report(jobId, index, wanted[index].Title, failures.Count);

                    var track = await resolving[index];

                    if (track is null)
                    {
                        failures.Add(wanted[index].Title);
                        continue;
                    }

                    var name = Unique(track.FileName, used);

                    var slot = archive.CreateEntry(name, CompressionLevel.NoCompression);

                    await using var slotStream = await slot.OpenAsync(cancellationToken);

                    try
                    {
                        await _downloadService.StreamWithAccelerationAsync(
                            track.Url,
                            slotStream,
                            null,
                            track.Proxy,
                            null,
                            cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Could not write {Name} into the archive", name);
                        failures.Add(wanted[index].Title);
                    }
                }

                if (failures.Count > 0 || omitted > 0)
                    await WriteManifestAsync(archive, listing, entries.Count, wanted.Count, omitted, failures);

                if (jobId is not null)
                    _progress.Report(jobId, wanted.Count, null, failures.Count);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Client disconnected while the archive was being written");

                if (jobId is not null)
                    _progress.Finish(jobId, ArchiveProgress.Failed);

                return StreamResult.ClientDisconnected();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed while writing the archive for {Url}", request.Url);

                if (jobId is not null)
                    _progress.Finish(jobId, ArchiveProgress.Failed);

                return request.Response.HasStarted
                    ? StreamResult.Success()
                    : StreamResult.ServerError("The archive could not be built.");
            }

            if (jobId is not null)
                _progress.Finish(jobId, ArchiveProgress.Done);

            return StreamResult.Success();
        }

        private void FailBeforeStarting(string? jobId)
        {
            if (jobId is null)
                return;

            _progress.Start(jobId, 0);
            _progress.Finish(jobId, ArchiveProgress.Failed);
        }

        private async Task<ResolvedTrack?> ResolveAsync(
            MediaInformationDto entry,
            SemaphoreSlim gate,
            CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);

            try
            {
                var resolved = await _mediator.Send(new GetMediaQuery(entry.RequestedUrl), cancellationToken);
                var audio = resolved.MediaItems?.FirstOrDefault()?.Audio;

                var key = audio?.Renditions?.FirstOrDefault()?.Key;

                if (string.IsNullOrWhiteSpace(key))
                    return null;

                var cached = _cacheService.GetCachedUrlWithFormat(key);

                if (cached is null)
                    return null;

                var (url, isAudio, mimeType, proxy, tags) = cached.Value;

                var extension = MediaFormats.ExtensionFor(mimeType)
                    ?? audio?.Renditions?.FirstOrDefault()?.FileExtension
                    ?? (isAudio ? ".mp3" : ".mp4");

                return new ResolvedTrack(url, proxy, MediaFileName.For(tags, extension));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation(ex, "Leaving {Title} out of the archive", entry.Title);
                return null;
            }
            finally
            {
                gate.Release();
            }
        }

        private static async Task WriteManifestAsync(
            ZipArchive archive,
            ResourceInformationDto listing,
            int total,
            int attempted,
            int omitted,
            IReadOnlyList<string> failures)
        {
            var note = new StringBuilder();

            note.AppendLine($"{listing.Title} - {listing.Author}");
            note.AppendLine();
            note.AppendLine($"This collection lists {total} tracks.");

            if (omitted > 0)
            {
                note.AppendLine();
                note.AppendLine($"An archive carries at most {MaxTracks} tracks, so the first {attempted} are");
                note.AppendLine($"included and the remaining {omitted} are not. Open the ones you want from");
                note.AppendLine("the listing to download them individually.");
            }

            if (failures.Count > 0)
            {
                note.AppendLine();
                note.AppendLine($"{failures.Count} of the {attempted} could not be downloaded:");
                note.AppendLine();

                foreach (var title in failures)
                    note.AppendLine($"  - {title}");
            }

            var entry = archive.CreateEntry("NOT-INCLUDED.txt", CompressionLevel.Optimal);

            await using var stream = await entry.OpenAsync();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);

            await writer.WriteAsync(note.ToString());
        }

        internal static string Unique(string name, HashSet<string> used)
        {
            if (used.Add(name))
                return name;

            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);

            for (var suffix = 2; ; suffix++)
            {
                var candidate = $"{stem} ({suffix}){extension}";

                if (used.Add(candidate))
                    return candidate;
            }
        }

        private record ResolvedTrack(string Url, string? Proxy, string FileName);
    }
}
