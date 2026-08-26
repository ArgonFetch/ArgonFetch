namespace ArgonFetch.Application.Services
{
    public interface IMaintenanceState
    {
        string? Activity { get; }

        IDisposable Begin(string activity);
    }

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
