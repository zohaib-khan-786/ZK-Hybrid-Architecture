namespace ZMASmall.Application.DTOs
{
    public class UpdateOrderDto
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerEmail { get; set; }
    }
}
