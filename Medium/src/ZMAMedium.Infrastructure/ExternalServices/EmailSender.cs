namespace ZMAMedium.Infrastructure.ExternalServices
{
    public class EmailSender
    {
        public Task SendAsync(string to, string subject, string body)
        {
            return Task.CompletedTask;
        }
    }
}
