using Microsoft.EntityFrameworkCore;
using ScalingReads.Core.Models;

namespace ScalingReads.Core.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {

    }

    public DbSet<Album> Albums => Set<Album>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Album>(e =>
        {
            e.HasIndex(a => a.Id);
            e.Property(a => a.Title).IsRequired();
            e.OwnsMany(a => a.Songs, sa =>
            {
                sa.WithOwner().HasForeignKey("AlbumId");
                sa.Property(s => s.Title).IsRequired();
            });
        });
    }
}
