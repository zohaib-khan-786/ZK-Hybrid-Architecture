using Microsoft.AspNetCore.Mvc;
using OrderService.Application;
using OrderService.Domain;

namespace OrderService.Presentation;

[Route("api/[controller]")]
[ApiController]
public class OrdersController : ControllerBase
{
    private readonly IOrderRepository _repository;

    public OrdersController(IOrderRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Order>>> GetAll()
    {
        var orders = await _repository.GetAllAsync();
        return Ok(orders);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Order>> GetById(int id)
    {
        var order = await _repository.GetByIdAsync(id);
        if (order is null)
            return NotFound();
        return Ok(order);
    }
}
