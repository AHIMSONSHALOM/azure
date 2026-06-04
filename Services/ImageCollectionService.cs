using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductHub_MVC.Data;

namespace ProductHub_MVC.Services
{
    public class ImageCollectionService : BackgroundService
    {
        private readonly ILogger<ImageCollectionService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

        public ImageCollectionService(ILogger<ImageCollectionService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Image Collection Service started.");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
                        var discoveryService = scope.ServiceProvider.GetRequiredService<InternetDiscoveryService>();

                        // Find 5 products with 0 registered images
                        var productsToEnrich = new List<(int Id, string Name, string Brand)>();
                        using (var conn = dbContext.CreateConnection())
                        {
                            string query = @"
                                SELECT TOP 5 F_PRODUCT_ID, F_PROD_NAME, F_BRAND 
                                FROM T_PRODUCTS p
                                WHERE NOT EXISTS (
                                    SELECT 1 FROM T_PRODUCT_IMAGES img WHERE img.ProductId = p.F_PRODUCT_ID
                                )";
                            using (var cmd = new SqlCommand(query, (SqlConnection)conn))
                            {
                                conn.Open();
                                using (var reader = cmd.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        productsToEnrich.Add((
                                            Convert.ToInt32(reader["F_PRODUCT_ID"]),
                                            reader["F_PROD_NAME"].ToString() ?? "",
                                            reader["F_BRAND"].ToString() ?? ""
                                        ));
                                    }
                                }
                            }
                        }

                        _logger.LogInformation($"Image Collection Service: Found {productsToEnrich.Count} products with missing images.");

                        foreach (var p in productsToEnrich)
                        {
                            if (stoppingToken.IsCancellationRequested) break;

                            _logger.LogInformation($"Image Collection Service: Fetching images for product '{p.Name}'...");
                            var enrichData = await discoveryService.FetchEnrichmentAsync(p.Name, p.Brand);
                            if (enrichData != null && enrichData.Images != null && enrichData.Images.Count > 0)
                            {
                                using (var conn = dbContext.CreateConnection())
                                {
                                    conn.Open();
                                    string primaryUrl = enrichData.Images[0];
                                    bool isFirst = true;

                                    foreach (var img in enrichData.Images.Take(5))
                                    {
                                        string insImgQ = @"
                                            INSERT INTO T_PRODUCT_IMAGES (ProductId, ImageUrl, Source, IsPrimary, CreatedAt) 
                                            VALUES (@ProductId, @ImageUrl, 'AI Scraper', @IsPrimary, GETDATE())";
                                        using (var imgCmd = new SqlCommand(insImgQ, (SqlConnection)conn))
                                        {
                                            imgCmd.Parameters.AddWithValue("@ProductId", p.Id);
                                            imgCmd.Parameters.AddWithValue("@ImageUrl", img);
                                            imgCmd.Parameters.AddWithValue("@IsPrimary", isFirst);
                                            imgCmd.ExecuteNonQuery();
                                        }
                                        isFirst = false;
                                    }

                                    // Update T_PRODUCTS primary ImageUrl
                                    string updQ = "UPDATE T_PRODUCTS SET F_IMAGE_URL = @Url WHERE F_PRODUCT_ID = @Id";
                                    using (var updCmd = new SqlCommand(updQ, (SqlConnection)conn))
                                    {
                                        updCmd.Parameters.AddWithValue("@Url", primaryUrl);
                                        updCmd.Parameters.AddWithValue("@Id", p.Id);
                                        updCmd.ExecuteNonQuery();
                                    }
                                }
                                _logger.LogInformation($"Image Collection Service: Successfully cached {enrichData.Images.Count} images for '{p.Name}'.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing Image Collection Service cycle.");
                }
                await Task.Delay(_interval, stoppingToken);
            }
            _logger.LogInformation("Image Collection Service stopped.");
        }
    }
}
