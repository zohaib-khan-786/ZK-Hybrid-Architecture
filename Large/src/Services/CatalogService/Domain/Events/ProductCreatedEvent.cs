using SharedKernel.Events;

namespace CatalogService.Domain.Events
{
    public class ProductCreatedEvent : IntegrationEvent
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
    }
}
