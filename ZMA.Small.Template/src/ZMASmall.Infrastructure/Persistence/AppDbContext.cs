using Microsoft.EntityFrameworkCore;
using ZMASmall.Domain.Entities;

namespace ZMASmall.Infrastructure.Persistence
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Product> Products => Set<Product>();
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
                entity.Property(p => p.Description).HasMaxLength(2000);
                entity.Property(p => p.Price).HasColumnType("decimal(18,2)");
                entity.Property(p => p.Category).HasMaxLength(100);
                entity.HasQueryFilter(p => p.IsActive);
            });

            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasKey(o => o.Id);
                entity.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)");
                entity.Property(o => o.CustomerName).HasMaxLength(200).IsRequired();
                entity.Property(o => o.CustomerEmail).HasMaxLength(200);
                entity.Property(o => o.Status).HasConversion<string>().HasMaxLength(50);
                entity.HasOne(o => o.Product)
                      .WithMany()
                      .HasForeignKey(o => o.ProductId);
            });
        }
    }
}
