using Microsoft.EntityFrameworkCore;
using ZMAMedium.Application.CatalogModule.Interfaces;
using ZMAMedium.Domain.Entities;
using ZMAMedium.Infrastructure.Persistence;

namespace ZMAMedium.Infrastructure.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly CatalogDbContext _context;

        public ProductRepository(CatalogDbContext context)
        {
            _context = context;
        }

        public async Task<Product?> GetByIdAsync(int id)
            => await _context.Products.FindAsync(id);

        public async Task<IEnumerable<Product>> GetAllAsync()
            => await _context.Products.ToListAsync();

        public async Task<Product> AddAsync(Product product)
        {
            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            return product;
        }

        public async Task UpdateAsync(Product product)
        {
            _context.Products.Update(product);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Product product)
        {
            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
        }
    }
}
