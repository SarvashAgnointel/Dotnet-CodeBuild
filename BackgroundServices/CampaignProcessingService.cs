namespace TravelAd_Api.BackgroundServices
{
    public class CampaignProcessingService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public CampaignProcessingService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dialler = scope.ServiceProvider.GetRequiredService<Dialler>();

            Console.WriteLine("Campaign Processing Service Started...");
            await dialler.ProcessCampaignsAsync();
        }
    }
}
