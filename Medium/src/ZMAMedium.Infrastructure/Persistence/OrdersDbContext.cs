using Microsoft.EntityFrameworkCore;
using ZMAMedium.Domain.Entities;
using ZMAMedium.Domain.Enums;

namespace ZMAMedium.Infrastructure.Persistence
{
    public class OrdersDbContext : DbContext
    {
        public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options) { }

        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
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
