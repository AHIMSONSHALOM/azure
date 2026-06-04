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
    public class ProductEnrichmentService : BackgroundService
    {
        private readonly ILogger<ProductEnrichmentService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

        public ProductEnrichmentService(ILogger<ProductEnrichmentService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Product Enrichment Service started.");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
                        var discoveryService = scope.ServiceProvider.GetRequiredService<InternetDiscoveryService>();

                        // Find 5 products with missing details
                        var productsToEnrich = new List<(int Id, string Name, string Brand)>();
                        using (var conn = dbContext.CreateConnection())
                        {
                            string query = @"
                                SELECT TOP 5 F_PRODUCT_ID, F_PROD_NAME, F_BRAND 
                                FROM T_PRODUCTS 
                                WHERE F_PROD_DESC IS NULL 
                                   OR CAST(F_PROD_DESC AS NVARCHAR(MAX)) = '' 
                                   OR CAST(F_PROD_DESC AS NVARCHAR(MAX)) = 'No description available.'";
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

                        _logger.LogInformation($"Product Enrichment Service: Found {productsToEnrich.Count} products to enrich.");

                        foreach (var p in productsToEnrich)
                        {
                            if (stoppingToken.IsCancellationRequested) break;

                            _logger.LogInformation($"Product Enrichment Service: Enriching product '{p.Name}' ({p.Brand})...");
                            var enrichData = await discoveryService.FetchEnrichmentAsync(p.Name, p.Brand);
                            if (enrichData != null)
                            {
                                string summaryText = enrichData.Reviews_Summary ?? "No summary available.";
                                using (var conn = dbContext.CreateConnection())
                                {
                                    string updateQ = @"
                                        UPDATE T_PRODUCTS 
                                        SET F_PROD_DESC = @Desc, 
                                            F_WIKIPEDIA_URL = @Wiki, 
                                            F_WEBSITE = @Website,
                                            F_AI_SUMMARY = @Summary
                                        WHERE F_PRODUCT_ID = @Id";
                                    using (var cmd = new SqlCommand(updateQ, (SqlConnection)conn))
                                    {
                                        cmd.Parameters.AddWithValue("@Desc", !string.IsNullOrEmpty(enrichData.Description) ? enrichData.Description : "No description available.");
                                        cmd.Parameters.AddWithValue("@Wiki", enrichData.WikipediaUrl ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@Website", enrichData.Website ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@Summary", summaryText);
                                        cmd.Parameters.AddWithValue("@Id", p.Id);
                                        conn.Open();
                                        cmd.ExecuteNonQuery();
                                    }

                                    // Save official website or Wikipedia as source
                                    if (!string.IsNullOrEmpty(enrichData.Website))
                                    {
                                        string srcQ = "INSERT INTO T_PRODUCT_SOURCES (ProductId, SourceUrl, SourceName) VALUES (@ProductId, @Url, 'Official Website')";
                                        using (var cmd = new SqlCommand(srcQ, (SqlConnection)conn))
                                        {
                                            cmd.Parameters.AddWithValue("@ProductId", p.Id);
                                            cmd.Parameters.AddWithValue("@Url", enrichData.Website);
                                            cmd.ExecuteNonQuery();
                                        }
                                    }
                                    if (!string.IsNullOrEmpty(enrichData.WikipediaUrl))
                                    {
                                        string srcQ = "INSERT INTO T_PRODUCT_SOURCES (ProductId, SourceUrl, SourceName) VALUES (@ProductId, @Url, 'Wikipedia')";
                                        using (var cmd = new SqlCommand(srcQ, (SqlConnection)conn))
                                        {
                                            cmd.Parameters.AddWithValue("@ProductId", p.Id);
                                            cmd.Parameters.AddWithValue("@Url", enrichData.WikipediaUrl);
                                            cmd.ExecuteNonQuery();
                                        }
                                    }
                                }
                                _logger.LogInformation($"Product Enrichment Service: Successfully enriched '{p.Name}'.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing Product Enrichment Service cycle.");
                }
                await Task.Delay(_interval, stoppingToken);
            }
            _logger.LogInformation("Product Enrichment Service stopped.");
        }
    }
}
