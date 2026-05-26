namespace CatalogService.Infrastructure.Services
{
    public class ExternalPricingService
    {
        public Task<decimal> GetExternalPriceAsync(int productId)
        {
            return Task.FromResult(0m);
        }
    }
}
