using ZMASmall.Application.DTOs;
using ZMASmall.Application.Exceptions;
using ZMASmall.Application.Interfaces;
using ZMASmall.Domain.Entities;

namespace ZMASmall.Application.Services
{
    public class ProductService : IProductService
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

        public async Task<ProductDto> CreateAsync(CreateProductDto dto)
        {
            var product = new Product
            {
                Name = dto.Name,
                Description = dto.Description,
                Price = dto.Price,
                StockQuantity = dto.StockQuantity,
                Category = dto.Category,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            var created = await _repository.AddAsync(product);
            return MapToDto(created);
        }

        public async Task<ProductDto> UpdateAsync(int id, UpdateProductDto dto)
        {
            var product = await _repository.GetByIdAsync(id);
            if (product is null)
                throw new NotFoundException($"Product with ID {id} not found.");

            product.Name = dto.Name;
            product.Description = dto.Description;
            product.Price = dto.Price;
            product.StockQuantity = dto.StockQuantity;
            product.Category = dto.Category;

            await _repository.UpdateAsync(product);
            return MapToDto(product);
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var product = await _repository.GetByIdAsync(id);
            if (product is null)
                return false;

            await _repository.DeleteAsync(product);
            return true;
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
