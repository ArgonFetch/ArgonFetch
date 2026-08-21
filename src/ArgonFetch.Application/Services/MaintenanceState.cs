namespace ArgonFetch.Application.Services
{
    public interface IMaintenanceState
    {
        /// <summary>
        /// What the app is busy with, or null when it is serving normally. Clients show this
        /// verbatim, so it is written for a reader rather than a log.
        /// </summary>
        string? Activity { get; }

        /// <summary>
        /// Marks the app as under maintenance until the returned handle is disposed.
        /// </summary>
        IDisposable Begin(string activity);
    }

    /// <summary>
    /// Tracks background work that makes fetching unreliable while it runs - swapping the yt-dlp
    /// binary being the one that actually happens. Without it a fetch landing mid-update fails
    /// with whatever error the half-replaced binary produces, which reads like a broken site
    /// rather than a busy server.
    /// </summary>
    public class MaintenanceState : IMaintenanceState
    {
        private readonly Lock _gate = new();
        private readonly List<string> _activities = [];

        public string? Activity
        {
            get
            {
                lock (_gate)
                {
                    return _activities.Count == 0 ? null : _activities[^1];
                }
            }
        }

        public IDisposable Begin(string activity)
        {
            lock (_gate)
            {
                _activities.Add(activity);
            }

            return new Scope(this, activity);
        }

        private void End(string activity)
        {
            lock (_gate)
            {
                _activities.Remove(activity);
            }
        }

        private sealed class Scope : IDisposable
        {
            private readonly MaintenanceState _state;
            private readonly string _activity;
            private bool _disposed;

            public Scope(MaintenanceState state, string activity)
            {
                _state = state;
                _activity = activity;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _state.End(_activity);
            }
        }
    }
}
