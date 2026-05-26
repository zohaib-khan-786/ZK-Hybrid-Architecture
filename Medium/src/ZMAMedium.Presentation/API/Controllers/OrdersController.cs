using Microsoft.AspNetCore.Mvc;
using ZMAMedium.Application.OrdersModule.DTOs;
using ZMAMedium.Application.OrdersModule.Interfaces;
using ZMAMedium.Application.Shared.Exceptions;

namespace ZMAMedium.Presentation.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        private readonly IOrderService _orderService;

        public OrdersController(IOrderService orderService)
        {
            _orderService = orderService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<OrderDto>>> GetAll()
        {
            var orders = await _orderService.GetAllAsync();
            return Ok(orders);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<OrderDto>> GetById(int id)
        {
            var order = await _orderService.GetByIdAsync(id);
            if (order is null)
                return NotFound();
            return Ok(order);
        }

        [HttpPost]
        public async Task<ActionResult<OrderDto>> Create(CreateOrderDto dto)
        {
            var order = await _orderService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<OrderDto>> Update(int id, UpdateOrderDto dto)
        {
            try
            {
                var order = await _orderService.UpdateAsync(id, dto);
                return Ok(order);
            }
            catch (NotFoundException)
            {
                return NotFound();
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _orderService.DeleteAsync(id);
            if (!result)
                return NotFound();
            return NoContent();
        }
    }
}
