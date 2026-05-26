namespace CatalogService.Domain.ValueObjects
{
    public class Money
    {
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "USD";
    }
}
