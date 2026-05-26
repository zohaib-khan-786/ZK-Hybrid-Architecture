using ZMAMedium.Application.OrdersModule.DTOs;

namespace ZMAMedium.Application.OrdersModule.Interfaces
{
    public interface IOrderService
    {
        Task<OrderDto?> GetByIdAsync(int id);
        Task<IEnumerable<OrderDto>> GetAllAsync();
        Task<OrderDto> CreateAsync(CreateOrderDto dto);
        Task<OrderDto> UpdateAsync(int id, UpdateOrderDto dto);
        Task<bool> DeleteAsync(int id);
    }
}
