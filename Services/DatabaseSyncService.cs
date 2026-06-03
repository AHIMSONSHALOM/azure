using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ProductHub_MVC.Services
{
    public class DatabaseSyncService : BackgroundService
    {
        private readonly ILogger<DatabaseSyncService> _logger;
        private readonly string _localConnectionString;
        private readonly string _azureConnectionString;
        private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(5);

        public DatabaseSyncService(ILogger<DatabaseSyncService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _localConnectionString = configuration.GetConnectionString("ProductHubSqlConnection") 
                ?? throw new InvalidOperationException("Local connection string not found.");
            _azureConnectionString = configuration.GetConnectionString("AzureSqlConnection") 
                ?? throw new InvalidOperationException("Azure connection string not found.");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Database Sync Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Starting database synchronization cycle...");
                    await SyncTableAsync("T_USERS", "UserId", stoppingToken);
                    await SyncTableAsync("T_PRODUCTS", "F_PRODUCT_ID", stoppingToken);
                    _logger.LogInformation("Database synchronization cycle completed successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred during database synchronization.");
                }

                await Task.Delay(_syncInterval, stoppingToken);
            }

            _logger.LogInformation("Database Sync Service is stopping.");
        }

        private async Task SyncTableAsync(string tableName, string primaryKeyColumn, CancellationToken stoppingToken)
        {
            // Note: In a production environment with millions of rows, 
            // you should use SQL Change Tracking or LastModified timestamps instead of full table scans.
            // This is a simplified bidirectional sync for demonstration purposes.

            _logger.LogInformation($"Syncing table {tableName} from Local to Azure...");
            await PushDataAsync(_localConnectionString, _azureConnectionString, tableName, primaryKeyColumn, stoppingToken);

            _logger.LogInformation($"Syncing table {tableName} from Azure to Local...");
            await PushDataAsync(_azureConnectionString, _localConnectionString, tableName, primaryKeyColumn, stoppingToken);
        }

        private async Task PushDataAsync(string sourceConnStr, string targetConnStr, string tableName, string primaryKeyColumn, CancellationToken stoppingToken)
        {
            using var sourceConn = new SqlConnection(sourceConnStr);
            using var targetConn = new SqlConnection(targetConnStr);

            await sourceConn.OpenAsync(stoppingToken);
            await targetConn.OpenAsync(stoppingToken);

            // 1. Read all data from source
            var selectCmd = new SqlCommand($"SELECT * FROM {tableName}", sourceConn);
            using var reader = await selectCmd.ExecuteReaderAsync(stoppingToken);

            while (await reader.ReadAsync(stoppingToken))
            {
                var pkValue = reader[primaryKeyColumn];
                
                // 2. Check if row exists in target
                var checkCmd = new SqlCommand($"SELECT COUNT(1) FROM {tableName} WHERE {primaryKeyColumn} = @pk", targetConn);
                checkCmd.Parameters.AddWithValue("@pk", pkValue);
                var exists = (int)await checkCmd.ExecuteScalarAsync(stoppingToken) > 0;

                if (!exists)
                {
                    // 3. Insert if missing (Simplified: Requires building dynamic insert based on columns)
                    // For an internship project, we log this or implement a column-aware insert.
                    _logger.LogInformation($"[Sync] Record {pkValue} missing in target {tableName}. Needs INSERT.");
                    // Implementation of dynamic insert goes here.
                }
                else
                {
                    // 4. Update if exists (Requires conflict resolution logic like LastModified)
                    // _logger.LogInformation($"[Sync] Record {pkValue} exists in target {tableName}. Needs UPDATE check.");
                }
            }
        }
    }
}
