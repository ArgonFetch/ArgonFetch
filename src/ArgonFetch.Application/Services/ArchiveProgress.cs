using Microsoft.Extensions.Caching.Memory;
using System.Text.Json.Serialization;

namespace ArgonFetch.Application.Services
{
    /// <summary>How far along an archive is.</summary>
    /// <param name="State">"building", "done" or "failed".</param>
    /// <param name="Total">Tracks the archive will carry.</param>
    /// <param name="Completed">Tracks written so far.</param>
    /// <param name="Current">Title being worked on, or null once there is nothing left.</param>
    /// <param name="Skipped">Tracks that could not be fetched and were left out.</param>
    public record ArchiveProgress(
        string State,
        int Total,
        int Completed,
        string? Current,
        int Skipped)
    {
        public const string Building = "building";
        public const string Done = "done";
        public const string Failed = "failed";

        // Read by the handler, not by the reader: State already says this, and sending both
        // invites a client to trust the wrong one.
        [JsonIgnore]
        public bool IsFinished => State is Done or Failed;
    }

    /// <summary>
    /// Where an archive reports how it is getting on, so the page that asked for one can show it.
    /// <para>
    /// The archive itself is fetched as a plain link, which is what lets the browser own the
    /// transfer and keep it after the page is left. That also means the page cannot see the
    /// download at all, so the progress is published here against the job id the page passed in,
    /// and read back over a separate request.
    /// </para>
    /// </summary>
    public interface IArchiveProgressTracker
    {
        void Start(string jobId, int total);
        void Report(string jobId, int completed, string? current, int skipped);
        void Finish(string jobId, string state);
        ArchiveProgress? Get(string jobId);
    }

    /// <summary>
    /// Progress held in memory, for as long as anyone could still be watching it.
    /// <para>
    /// Not durable on purpose: it describes a transfer that is itself only alive as long as the
    /// request is, so outliving the process would tell a reader nothing true. One process only -
    /// behind more than one replica the page could ask the instance that is not building its
    /// archive, and would be told the job is unknown.
    /// </para>
    /// </summary>
    public class ArchiveProgressTracker : IArchiveProgressTracker
    {
        // Long enough to cover a slow archive plus a reader that reconnects, short enough that
        // abandoned jobs do not accumulate.
        private static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);

        // Kept briefly after the end so a reader that polls just after the last write still
        // learns how it finished rather than finding nothing and guessing.
        private static readonly TimeSpan RetentionAfterFinish = TimeSpan.FromMinutes(2);

        private readonly IMemoryCache _cache;

        public ArchiveProgressTracker(IMemoryCache cache) => _cache = cache;

        public void Start(string jobId, int total) =>
            Set(jobId, new ArchiveProgress(ArchiveProgress.Building, total, 0, null, 0), Retention);

        public void Report(string jobId, int completed, string? current, int skipped)
        {
            var existing = Get(jobId);

            if (existing is null || existing.IsFinished)
                return;

            Set(jobId, existing with { Completed = completed, Current = current, Skipped = skipped }, Retention);
        }

        public void Finish(string jobId, string state)
        {
            var existing = Get(jobId);

            if (existing is null)
                return;

            Set(jobId, existing with { State = state, Current = null }, RetentionAfterFinish);
        }

        public ArchiveProgress? Get(string jobId) =>
            string.IsNullOrWhiteSpace(jobId) ? null : _cache.Get<ArchiveProgress>(Key(jobId));

        private void Set(string jobId, ArchiveProgress progress, TimeSpan retention) =>
            _cache.Set(Key(jobId), progress, retention);

        private static string Key(string jobId) => $"archive-progress:{jobId}";
    }
}
