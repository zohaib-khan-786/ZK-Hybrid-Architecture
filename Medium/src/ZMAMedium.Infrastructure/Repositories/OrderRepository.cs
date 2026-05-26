using Microsoft.EntityFrameworkCore;
using ZMAMedium.Application.OrdersModule.Interfaces;
using ZMAMedium.Domain.Entities;
using ZMAMedium.Infrastructure.Persistence;

namespace ZMAMedium.Infrastructure.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly OrdersDbContext _context;

        public OrderRepository(OrdersDbContext context)
        {
            _context = context;
        }

        public async Task<Order?> GetByIdAsync(int id)
            => await _context.Orders
                .Include(o => o.Product)
                .FirstOrDefaultAsync(o => o.Id == id);

        public async Task<IEnumerable<Order>> GetAllAsync()
            => await _context.Orders
                .Include(o => o.Product)
                .ToListAsync();

        public async Task<Order> AddAsync(Order order)
        {
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();
            return order;
        }

        public async Task UpdateAsync(Order order)
        {
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Order order)
        {
            _context.Orders.Remove(order);
            await _context.SaveChangesAsync();
        }
    }
}
