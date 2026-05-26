using ZMASmall.Application.DTOs;
using ZMASmall.Application.Exceptions;
using ZMASmall.Application.Interfaces;
using ZMASmall.Domain.Entities;
using ZMASmall.Domain.Enums;

namespace ZMASmall.Application.Services
{
    public class OrderService : IOrderService
    {
        private readonly IOrderRepository _repository;

        public OrderService(IOrderRepository repository)
        {
            _repository = repository;
        }

        public async Task<OrderDto?> GetByIdAsync(int id)
        {
            var order = await _repository.GetByIdAsync(id);
            return order is null ? null : MapToDto(order);
        }

        public async Task<IEnumerable<OrderDto>> GetAllAsync()
        {
            var orders = await _repository.GetAllAsync();
            return orders.Select(MapToDto);
        }

        public async Task<OrderDto> CreateAsync(CreateOrderDto dto)
        {
            var order = new Order
            {
                ProductId = dto.ProductId,
                Quantity = dto.Quantity,
                CustomerName = dto.CustomerName,
                CustomerEmail = dto.CustomerEmail,
                TotalAmount = 0,
                Status = OrderStatus.Pending,
                OrderedAt = DateTime.UtcNow
            };

            var created = await _repository.AddAsync(order);
            return MapToDto(created);
        }

        public async Task<OrderDto> UpdateAsync(int id, UpdateOrderDto dto)
        {
            var order = await _repository.GetByIdAsync(id);
            if (order is null)
                throw new NotFoundException($"Order with ID {id} not found.");

            order.ProductId = dto.ProductId;
            order.Quantity = dto.Quantity;
            order.CustomerName = dto.CustomerName;
            order.CustomerEmail = dto.CustomerEmail;

            await _repository.UpdateAsync(order);
            return MapToDto(order);
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var order = await _repository.GetByIdAsync(id);
            if (order is null)
                return false;

            await _repository.DeleteAsync(order);
            return true;
        }

        private static OrderDto MapToDto(Order order) => new()
        {
            Id = order.Id,
            ProductId = order.ProductId,
            ProductName = order.Product?.Name ?? string.Empty,
            Quantity = order.Quantity,
            TotalAmount = order.TotalAmount,
            Status = order.Status.ToString(),
            OrderedAt = order.OrderedAt,
            CustomerName = order.CustomerName
        };
    }
}
