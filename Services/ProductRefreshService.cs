using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductHub_MVC.Data;

namespace ProductHub_MVC.Services
{
    public class ProductRefreshService : BackgroundService
    {
        private readonly ILogger<ProductRefreshService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeSpan _interval = TimeSpan.FromHours(12);

        public ProductRefreshService(ILogger<ProductRefreshService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Product Refresh Service started.");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
                        var discoveryService = scope.ServiceProvider.GetRequiredService<InternetDiscoveryService>();

                        // Find products that haven't been updated/launched in the last 7 days, or have null launch dates
                        var productsToRefresh = new List<(int Id, string Name, string Brand)>();
                        using (var conn = dbContext.CreateConnection())
                        {
                            string query = @"
                                SELECT TOP 5 F_PRODUCT_ID, F_PROD_NAME, F_BRAND 
                                FROM T_PRODUCTS 
                                WHERE F_LAUNCH_DATE IS NULL 
                                   OR F_LAUNCH_DATE < DATEADD(day, -7, GETDATE())";
                            using (var cmd = new SqlCommand(query, (SqlConnection)conn))
                            {
                                conn.Open();
                                using (var reader = cmd.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        productsToRefresh.Add((
                                            Convert.ToInt32(reader["F_PRODUCT_ID"]),
                                            reader["F_PROD_NAME"].ToString() ?? "",
                                            reader["F_BRAND"].ToString() ?? ""
                                        ));
                                    }
                                }
                            }
                        }

                        _logger.LogInformation($"Product Refresh Service: Found {productsToRefresh.Count} products to refresh.");

                        foreach (var p in productsToRefresh)
                        {
                            if (stoppingToken.IsCancellationRequested) break;

                            _logger.LogInformation($"Product Refresh Service: Refreshing stale product '{p.Name}'...");
                            var enrichData = await discoveryService.FetchEnrichmentAsync(p.Name, p.Brand);
                            if (enrichData != null)
                            {
                                using (var conn = dbContext.CreateConnection())
                                {
                                    string updateQ = @"
                                        UPDATE T_PRODUCTS 
                                        SET F_PROD_DESC = @Desc, 
                                            F_WIKIPEDIA_URL = @Wiki, 
                                            F_WEBSITE = @Website,
                                            F_LAUNCH_DATE = GETDATE()
                                        WHERE F_PRODUCT_ID = @Id";
                                    using (var cmd = new SqlCommand(updateQ, (SqlConnection)conn))
                                    {
                                        cmd.Parameters.AddWithValue("@Desc", !string.IsNullOrEmpty(enrichData.Description) ? enrichData.Description : "No description available.");
                                        cmd.Parameters.AddWithValue("@Wiki", enrichData.WikipediaUrl ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@Website", enrichData.Website ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@Id", p.Id);
                                        conn.Open();
                                        cmd.ExecuteNonQuery();
                                    }
                                }
                                _logger.LogInformation($"Product Refresh Service: Successfully updated '{p.Name}' data.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing Product Refresh Service cycle.");
                }
                await Task.Delay(_interval, stoppingToken);
            }
            _logger.LogInformation("Product Refresh Service stopped.");
        }
    }
}
