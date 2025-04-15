using System.Diagnostics;

namespace TravelAd_Api.BackgroundServices
{
    public class MemoryCleanupService : BackgroundService
    {
        private readonly IConfiguration _configuration;
        public MemoryCleanupService(IConfiguration configuration) {
            _configuration = configuration;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                int CleanerInterval = Convert.ToInt32(_configuration["MemoryCleanupService:CleaningIntervalInMinutes"]);
                int MaxMemory = Convert.ToInt32(_configuration["MemoryCleanupService:MaxMemoryLimit"]);
                long memoryUsage = Process.GetCurrentProcess().PrivateMemorySize64 / (1024 * 1024);
                Console.WriteLine($"Current Memory Usage(No Cleanup): {memoryUsage} MB");
                if (memoryUsage > MaxMemory) // Only clean up if memory > MaxMemory
                {
                    Console.WriteLine("Running Garbage Collection...");
                    //_logger.LogInformation("Running Garbage Collection...");
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                    Console.WriteLine($"Current Memory Usage(After Cleanup): {memoryUsage} MB");
                }
                //_logger.LogInformation($"Current Memory Usage: {memoryUsage} MB");
                await Task.Delay(TimeSpan.FromMinutes(CleanerInterval), stoppingToken);
            }
        }

    }
}
