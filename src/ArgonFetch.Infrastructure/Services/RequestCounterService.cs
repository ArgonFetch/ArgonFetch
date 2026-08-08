using ArgonFetch.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Infrastructure.Services
{
    /// <summary>
    /// Keeps the single request-counter row up to date.
    /// <para>
    /// The increment is done in the database rather than by reading, adding one and saving,
    /// so concurrent requests cannot overwrite each other's count.
    /// </para>
    /// </summary>
    public class RequestCounterService : IRequestCounterService
    {
        private readonly ArgonFetchDbContext _dbContext;
        private readonly ILogger<RequestCounterService> _logger;

        public RequestCounterService(ArgonFetchDbContext dbContext, ILogger<RequestCounterService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task IncrementAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var updated = await _dbContext.RequestCounters
                    .Where(c => c.Id == 1)
                    .ExecuteUpdateAsync(
                        set => set
                            .SetProperty(c => c.TotalRequests, c => c.TotalRequests + 1)
                            .SetProperty(c => c.LastRequestAtUtc, _ => DateTime.UtcNow),
                        cancellationToken);

                if (updated == 0)
                {
                    // First request on a fresh database, or the seed row was removed.
                    _dbContext.RequestCounters.Add(new Domain.RequestCounter
                    {
                        Id = 1,
                        TotalRequests = 1,
                        LastRequestAtUtc = DateTime.UtcNow
                    });

                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // A counter is not worth failing a download over.
                _logger.LogWarning(ex, "Could not record the request in the counter");
            }
        }

        public async Task<long> GetTotalAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _dbContext.RequestCounters
                    .Where(c => c.Id == 1)
                    .Select(c => c.TotalRequests)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read the request counter");
                return 0;
            }
        }
    }
}
