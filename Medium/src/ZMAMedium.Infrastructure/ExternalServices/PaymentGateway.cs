namespace ZMAMedium.Infrastructure.ExternalServices
{
    public class PaymentGateway
    {
        public Task<bool> ProcessPaymentAsync(decimal amount, string cardToken)
        {
            return Task.FromResult(true);
        }
    }
}
