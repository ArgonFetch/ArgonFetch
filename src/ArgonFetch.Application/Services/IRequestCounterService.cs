namespace ArgonFetch.Application.Services
{
    public interface IRequestCounterService
    {
        /// <summary>
        /// Records one served media request. Never throws: a counter failure must not fail
        /// the download the user actually asked for.
        /// </summary>
        Task IncrementAsync(CancellationToken cancellationToken = default);

        Task<long> GetTotalAsync(CancellationToken cancellationToken = default);
    }
}
