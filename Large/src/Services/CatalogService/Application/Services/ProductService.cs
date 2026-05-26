using CatalogService.Application.DTOs;
using CatalogService.Application.Interfaces;
using CatalogService.Domain.Entities;

namespace CatalogService.Application.Services
{
    public class ProductService
    {
        private readonly IProductRepository _repository;

        public ProductService(IProductRepository repository)
        {
            _repository = repository;
        }

        public async Task<ProductDto?> GetByIdAsync(int id)
        {
            var product = await _repository.GetByIdAsync(id);
            return product is null ? null : MapToDto(product);
        }

        public async Task<IEnumerable<ProductDto>> GetAllAsync()
        {
            var products = await _repository.GetAllAsync();
            return products.Select(MapToDto);
        }

        public async Task<ProductDto> CreateAsync(string name, string? description, decimal price, int stock, string category)
        {
            var product = new Product
            {
                Name = name,
                Description = description,
                Price = price,
                StockQuantity = stock,
                Category = category,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            var created = await _repository.AddAsync(product);
            return MapToDto(created);
        }

        private static ProductDto MapToDto(Product product) => new()
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            StockQuantity = product.StockQuantity,
            Category = product.Category
        };
    }
}
