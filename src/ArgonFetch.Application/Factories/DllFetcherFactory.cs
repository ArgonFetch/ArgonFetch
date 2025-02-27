using ArgonFetch.Application.Interfaces;

namespace ArgonFetch.Application.Factories
{
    public class DllFetcherFactory
    {
        private readonly IEnumerable<IDllFetcher> _fetchers;

        public DllFetcherFactory(IEnumerable<IDllFetcher> fetchers)
        {
            _fetchers = fetchers;
        }

        public IDllFetcher? GetFetcher(string type)
        {
            return _fetchers.SingleOrDefault(f => f.GetType().Name.StartsWith(type, StringComparison.OrdinalIgnoreCase));
        }
    }
}
