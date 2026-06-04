using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ProductHub_MVC.Services
{
    public class ProductSyncService : BackgroundService
    {
        private readonly ILogger<ProductSyncService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeSpan _interval = TimeSpan.FromHours(1);

        public ProductSyncService(ILogger<ProductSyncService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Product Sync Service started.");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var discoveryService = scope.ServiceProvider.GetRequiredService<InternetDiscoveryService>();
                        _logger.LogInformation("Product Sync Service: Starting live feeds sync...");
                        int count = await discoveryService.SyncLiveFeedsAsync();
                        _logger.LogInformation($"Product Sync Service: Live feeds sync completed. Added {count} products.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing Product Sync Service cycle.");
                }
                await Task.Delay(_interval, stoppingToken);
            }
            _logger.LogInformation("Product Sync Service stopped.");
        }
    }
}
