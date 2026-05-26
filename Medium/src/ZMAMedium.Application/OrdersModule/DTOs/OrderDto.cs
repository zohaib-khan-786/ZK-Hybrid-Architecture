namespace ZMAMedium.Application.OrdersModule.DTOs
{
    public class OrderDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime OrderedAt { get; set; }
        public string CustomerName { get; set; } = string.Empty;
    }
}
