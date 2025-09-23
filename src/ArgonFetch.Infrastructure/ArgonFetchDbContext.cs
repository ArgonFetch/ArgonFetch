using ArgonFetch.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ArgonFetch.Infrastructure
{
    public class ArgonFetchDbContext : DbContext
    {
        public DbSet<UrlReference> UrlReference { get; set; }

        public ArgonFetchDbContext(DbContextOptions<ArgonFetchDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
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
