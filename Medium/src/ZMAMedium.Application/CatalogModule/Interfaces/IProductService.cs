using ZMAMedium.Application.CatalogModule.DTOs;

namespace ZMAMedium.Application.CatalogModule.Interfaces
{
    public interface IProductService
    {
        Task<ProductDto?> GetByIdAsync(int id);
        Task<IEnumerable<ProductDto>> GetAllAsync();
        Task<ProductDto> CreateAsync(CreateProductDto dto);
        Task<ProductDto> UpdateAsync(int id, UpdateProductDto dto);
        Task<bool> DeleteAsync(int id);
    }
}
