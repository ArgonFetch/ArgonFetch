using ArgonFetch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ArgonFetch.Infrastructure
{
    public class ArgonFetchDbContext : DbContext
    {
        public DbSet<UrlReference> UrlReference { get; set; }
        public DbSet<RequestCounter> RequestCounters { get; set; }

        public ArgonFetchDbContext(DbContextOptions<ArgonFetchDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // One row, seeded so the first read works before any request is served.
            modelBuilder.Entity<RequestCounter>().HasData(new RequestCounter
            {
                Id = 1,
                TotalRequests = 0,
                LastRequestAtUtc = DateTime.MinValue
            });
        }
    }

    public class ArgonFetchDbContextFactory : IDesignTimeDbContextFactory<ArgonFetchDbContext>
    {
        public ArgonFetchDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ArgonFetchDbContext>();
            optionsBuilder.UseNpgsql();
            return new ArgonFetchDbContext(optionsBuilder.Options);
        }
    }
}
