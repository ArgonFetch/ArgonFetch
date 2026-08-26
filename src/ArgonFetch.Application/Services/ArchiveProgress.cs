using Microsoft.Extensions.Caching.Memory;
using System.Text.Json.Serialization;

namespace ArgonFetch.Application.Services
{
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

        [JsonIgnore]
        public bool IsFinished => State is Done or Failed;
    }

    public interface IArchiveProgressTracker
    {
        void Start(string jobId, int total);
        void Report(string jobId, int completed, string? current, int skipped);
        void Finish(string jobId, string state);
        ArchiveProgress? Get(string jobId);
    }

    public class ArchiveProgressTracker : IArchiveProgressTracker
    {
        private static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);

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
