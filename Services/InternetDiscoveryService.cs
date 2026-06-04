using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProductHub_MVC.Models;

namespace ProductHub_MVC.Services
{
    public class InternetDiscoveryService
    {
        private readonly ILogger<InternetDiscoveryService> _logger;
        private readonly string _connectionString;
        private readonly HttpClient _httpClient;
        private readonly string _aiApiUrl = "http://localhost:8000";

        private static readonly Dictionary<string, (string Url, string DefaultCategory)> Feeds = new()
        {
            { "TechCrunch", ("https://techcrunch.com/category/gadgets/feed/", "Electronics") },
            { "Hacker News", ("https://news.ycombinator.com/rss", "Mobiles") },
            { "Product Hunt", ("https://www.producthunt.com/feed", "Electronics") }
        };

        public InternetDiscoveryService(ILogger<InternetDiscoveryService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("ProductHubSqlConnection")
                ?? throw new InvalidOperationException("Connection string not found.");

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ProductHubDiscoveryService/2.0 (contact@producthub.local; educational purposes)");
        }

        public async Task<int> SyncLiveFeedsAsync()
        {
            _logger.LogInformation("Starting live RSS feeds sync & background enrichment...");
            int addedCount = 0;

            foreach (var feed in Feeds)
            {
                string sourceName = feed.Key;
                var (feedUrl, defaultCategory) = feed.Value;

                try
                {
                    _logger.LogInformation($"Fetching RSS feed from {sourceName}: {feedUrl}");
                    var response = await _httpClient.GetAsync(feedUrl);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning($"Failed to download feed from {sourceName}. Status code: {response.StatusCode}");
                        continue;
                    }

                    string xmlContent = await response.Content.ReadAsStringAsync();
                    var feedProducts = ParseRssFeed(xmlContent, sourceName, defaultCategory);
                    _logger.LogInformation($"Parsed {feedProducts.Count} items from {sourceName} RSS feed.");

                    foreach (var product in feedProducts)
                    {
                        // Check if product already exists in database
                        if (ProductExists(product.ProductName))
                        {
                            continue;
                        }

                        // A. Background Enrichment: Fetch Category & Subcategory classification
                        var (category, subcategory) = await FetchClassificationAsync(product.ProductName, product.ProductDescription ?? "");
                        product.Category = category;
                        product.Subcategory = subcategory;

                        // B. Background Enrichment: Fetch full metadata, images, and related details
                        _logger.LogInformation($"Enriching product '{product.ProductName}' via AI service...");
                        var enrichData = await FetchEnrichmentAsync(product.ProductName, product.Brand);
                        if (enrichData != null)
                        {
                            if (!string.IsNullOrEmpty(enrichData.Description))
                            {
                                product.ProductDescription = enrichData.Description;
                            }
                            if (!string.IsNullOrEmpty(enrichData.WikipediaUrl))
                            {
                                product.WikipediaUrl = enrichData.WikipediaUrl;
                            }
                            if (!string.IsNullOrEmpty(enrichData.Website))
                            {
                                product.Website = enrichData.Website;
                            }
                            
                            // Load images list
                            if (enrichData.Images != null && enrichData.Images.Count > 0)
                            {
                                product.ImageUrl = enrichData.Images[0]; // Set primary image
                                foreach (var img in enrichData.Images)
                                {
                                    product.ProductImages.Add(new ProductImage
                                    {
                                        ImageUrl = img,
                                        Source = "AI Enrichment",
                                        IsPrimary = (img == product.ImageUrl)
                                    });
                                }
                            }
                            else if (!string.IsNullOrEmpty(product.ImageUrl))
                            {
                                product.ProductImages.Add(new ProductImage
                                {
                                    ImageUrl = product.ImageUrl,
                                    Source = "RSS Feed",
                                    IsPrimary = true
                                });
                            }

                            // Load source website info
                            if (!string.IsNullOrEmpty(product.ArticleUrl))
                            {
                                product.ProductSources.Add(new ProductSource
                                {
                                    SourceUrl = product.ArticleUrl,
                                    SourceName = sourceName
                                });
                            }
                            if (!string.IsNullOrEmpty(product.WikipediaUrl))
                            {
                                product.ProductSources.Add(new ProductSource
                                {
                                    SourceUrl = product.WikipediaUrl,
                                    SourceName = "Wikipedia"
                                });
                            }
                            if (!string.IsNullOrEmpty(product.Website))
                            {
                                product.ProductSources.Add(new ProductSource
                                {
                                    SourceUrl = product.Website.StartsWith("http") ? product.Website : $"https://{product.Website}",
                                    SourceName = "Official Website"
                                });
                            }
                        }

                        // Insert product into database with related images and sources
                        bool inserted = InsertProduct(product);
                        if (inserted)
                        {
                            addedCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error syncing feed from {sourceName}");
                }
            }

            _logger.LogInformation($"RSS sync completed. Added {addedCount} new live products.");
            return addedCount;
        }

        private List<Product> ParseRssFeed(string xmlContent, string sourceName, string defaultCategory)
        {
            var products = new List<Product>();
            try
            {
                var doc = XDocument.Parse(xmlContent);
                var items = doc.Descendants("item");

                foreach (var item in items)
                {
                    string rawTitle = item.Element("title")?.Value ?? string.Empty;
                    string link = item.Element("link")?.Value ?? string.Empty;
                    string description = item.Element("description")?.Value ?? string.Empty;
                    string pubDateStr = item.Element("pubDate")?.Value ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(rawTitle)) continue;

                    string productName = CleanProductName(rawTitle);
                    if (productName.Length > 255) productName = productName.Substring(0, 252) + "...";

                    DateTime? launchDate = null;
                    if (DateTime.TryParse(pubDateStr, out DateTime parsedDate))
                    {
                        launchDate = parsedDate;
                    }

                    string? imageUrl = null;
                    var mediaContent = item.Elements().FirstOrDefault(e => e.Name.LocalName == "content" && e.Name.NamespaceName.Contains("mrss"));
                    var mediaThumbnail = item.Elements().FirstOrDefault(e => e.Name.LocalName == "thumbnail" && e.Name.NamespaceName.Contains("mrss"));
                    imageUrl = mediaContent?.Attribute("url")?.Value ?? mediaThumbnail?.Attribute("url")?.Value;

                    if (string.IsNullOrEmpty(imageUrl) && !string.IsNullOrEmpty(description))
                    {
                        var match = Regex.Match(description, @"<img[^>]+src=""([^""]+)""");
                        if (match.Success)
                        {
                            imageUrl = match.Groups[1].Value;
                        }
                    }

                    string cleanedDesc = StripHtmlTags(description);
                    if (cleanedDesc.Length > 1000)
                    {
                        cleanedDesc = cleanedDesc.Substring(0, 997) + "...";
                    }

                    double price = 0.0;
                    var priceMatch = Regex.Match(rawTitle + " " + cleanedDesc, @"\$([0-9]+(?:\.[0-9]{2})?)");
                    if (priceMatch.Success && double.TryParse(priceMatch.Groups[1].Value, out double parsedPrice))
                    {
                        price = parsedPrice;
                    }

                    double rating = 4.0;
                    int wordCount = cleanedDesc.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    rating = Math.Clamp(3.5 + (wordCount % 15) * 0.1, 3.5, 5.0);
                    rating = Math.Round(rating, 1);

                    string website = sourceName;
                    try
                    {
                        if (Uri.TryCreate(link, UriKind.Absolute, out var uri))
                        {
                            website = uri.Host;
                        }
                    }
                    catch { }

                    products.Add(new Product
                    {
                        ProductName = productName,
                        Brand = sourceName,
                        Quantity = "Live Item",
                        Price = price,
                        ProductDescription = string.IsNullOrEmpty(cleanedDesc) ? "No description available." : cleanedDesc,
                        ProductRating = rating,
                        Category = defaultCategory,
                        LaunchDate = launchDate,
                        Website = website,
                        ArticleUrl = link,
                        ImageUrl = imageUrl
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to parse RSS XML feed contents for {sourceName}");
            }
            return products;
        }

        private async Task<(string Category, string Subcategory)> FetchClassificationAsync(string name, string description)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"{_aiApiUrl}/classify", new { name, description });
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ClassificationResponse>();
                    if (result != null && !string.IsNullOrEmpty(result.Category) && !string.IsNullOrEmpty(result.Subcategory))
                    {
                        return (result.Category, result.Subcategory);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"AI category classification endpoint failed. Fallback to local rule parser: {ex.Message}");
            }

            // Fallback to local rules
            return CategoryHelper.ClassifyFallback(name, description);
        }

        public async Task<EnrichmentResult?> FetchEnrichmentAsync(string name, string brand)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"{_aiApiUrl}/enrich", new { name, brand });
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<EnrichmentResult>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"AI enrichment endpoint failed for '{name}': {ex.Message}");
            }
            return null;
        }

        private string CleanProductName(string rawTitle)
        {
            string name = rawTitle;
            var showHnRegex = new Regex(@"^(?:Show HN|Ask HN|Launch HN)\s*:\s*", RegexOptions.IgnoreCase);
            name = showHnRegex.Replace(name, "");
            name = Regex.Replace(name, @"\s+review\s*$", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"^Review:\s*", "", RegexOptions.IgnoreCase);
            return name.Trim();
        }

        private string StripHtmlTags(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            string clean = Regex.Replace(html, "<.*?>", string.Empty);
            clean = System.Web.HttpUtility.HtmlDecode(clean);
            return clean.Trim();
        }

        private bool ProductExists(string productName)
        {
            using var conn = new SqlConnection(_connectionString);
            string query = "SELECT COUNT(1) FROM T_PRODUCTS WHERE F_PROD_NAME = @Name";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Name", productName);
            
            conn.Open();
            int count = (int)cmd.ExecuteScalar();
            return count > 0;
        }

        private void ResolveBrandAndCategory(SqlConnection conn, string brandName, string categoryName, out int? brandId, out int? categoryId)
        {
            brandId = null;
            categoryId = null;

            if (string.IsNullOrEmpty(brandName)) brandName = "Generic";
            if (string.IsNullOrEmpty(categoryName)) categoryName = "Electronics";

            // Resolve Brand
            string checkBrand = "SELECT BrandId FROM T_BRANDS WHERE BrandName = @B";
            using (var cmd = new SqlCommand(checkBrand, conn))
            {
                cmd.Parameters.AddWithValue("@B", brandName);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value)
                {
                    brandId = Convert.ToInt32(res);
                }
                else
                {
                    string insertBrand = "INSERT INTO T_BRANDS (BrandName) VALUES (@B); SELECT SCOPE_IDENTITY();";
                    using (var insCmd = new SqlCommand(insertBrand, conn))
                    {
                        insCmd.Parameters.AddWithValue("@B", brandName);
                        brandId = Convert.ToInt32(insCmd.ExecuteScalar());
                    }
                }
            }

            // Resolve Category
            string checkCat = "SELECT CategoryId FROM T_CATEGORIES WHERE CategoryName = @C";
            using (var cmd = new SqlCommand(checkCat, conn))
            {
                cmd.Parameters.AddWithValue("@C", categoryName);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value)
                {
                    categoryId = Convert.ToInt32(res);
                }
                else
                {
                    string insertCat = "INSERT INTO T_CATEGORIES (CategoryName) VALUES (@C); SELECT SCOPE_IDENTITY();";
                    using (var insCmd = new SqlCommand(insertCat, conn))
                    {
                        insCmd.Parameters.AddWithValue("@C", categoryName);
                        categoryId = Convert.ToInt32(insCmd.ExecuteScalar());
                    }
                }
            }
        }

        public bool InsertProduct(Product p)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();

                ResolveBrandAndCategory(conn, p.Brand, p.Category, out int? bId, out int? cId);
                p.BrandId = bId;
                p.CategoryId = cId;

                // 1. Insert product and select SCOPE_IDENTITY() to get new ProductId
                string query = @"
                    INSERT INTO T_PRODUCTS 
                    (F_PROD_NAME, F_BRAND, F_BRAND_ID, F_QTY, F_PRICE, F_PROD_DESC, F_PROD_RATING, F_CATEGORY, F_CATEGORY_ID, F_SUBCATEGORY, F_LAUNCH_DATE, F_WEBSITE, F_AI_SUMMARY, F_WIKIPEDIA_URL, F_IMAGE_URL, F_ARTICLE_URL)
                    VALUES 
                    (@Name, @Brand, @BrandId, @Qty, @Price, @Desc, @Rating, @Category, @CategoryId, @Subcategory, @LaunchDate, @Website, @AiSummary, @WikipediaUrl, @ImageUrl, @ArticleUrl);
                    SELECT SCOPE_IDENTITY();";

                int newProductId;
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Name", p.ProductName);
                    cmd.Parameters.AddWithValue("@Brand", p.Brand);
                    cmd.Parameters.AddWithValue("@BrandId", p.BrandId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Qty", p.Quantity);
                    cmd.Parameters.AddWithValue("@Price", p.Price);
                    cmd.Parameters.AddWithValue("@Desc", p.ProductDescription ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Rating", p.ProductRating);
                    cmd.Parameters.AddWithValue("@Category", p.Category ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@CategoryId", p.CategoryId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Subcategory", p.Subcategory ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@LaunchDate", p.LaunchDate ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Website", p.Website ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@AiSummary", p.AiSummary ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@WikipediaUrl", p.WikipediaUrl ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ImageUrl", p.ImageUrl ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ArticleUrl", p.ArticleUrl ?? (object)DBNull.Value);

                    newProductId = Convert.ToInt32(cmd.ExecuteScalar());
                    p.ProductId = newProductId;
                }

                // 2. Cache multiple product images
                if (p.ProductImages != null && p.ProductImages.Count > 0)
                {
                    string imgQuery = "INSERT INTO T_PRODUCT_IMAGES (ProductId, ImageUrl, Source, IsPrimary) VALUES (@ProductId, @ImageUrl, @Source, @IsPrimary)";
                    foreach (var img in p.ProductImages)
                    {
                        using var imgCmd = new SqlCommand(imgQuery, conn);
                        imgCmd.Parameters.AddWithValue("@ProductId", newProductId);
                        imgCmd.Parameters.AddWithValue("@ImageUrl", img.ImageUrl);
                        imgCmd.Parameters.AddWithValue("@Source", img.Source ?? "AI Scraper");
                        imgCmd.Parameters.AddWithValue("@IsPrimary", img.IsPrimary);
                        imgCmd.ExecuteNonQuery();
                    }
                }

                // 3. Cache product source websites
                if (p.ProductSources != null && p.ProductSources.Count > 0)
                {
                    string srcQuery = "INSERT INTO T_PRODUCT_SOURCES (ProductId, SourceUrl, SourceName) VALUES (@ProductId, @SourceUrl, @SourceName)";
                    foreach (var src in p.ProductSources)
                    {
                        using var srcCmd = new SqlCommand(srcQuery, conn);
                        srcCmd.Parameters.AddWithValue("@ProductId", newProductId);
                        srcCmd.Parameters.AddWithValue("@SourceUrl", src.SourceUrl);
                        srcCmd.Parameters.AddWithValue("@SourceName", src.SourceName);
                        srcCmd.ExecuteNonQuery();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to insert and cache enriched product: '{p.ProductName}'");
                return false;
            }
        }

        private class ClassificationResponse
        {
            public string Category { get; set; } = string.Empty;
            public string Subcategory { get; set; } = string.Empty;
        }

        public class EnrichmentResult
        {
            public string Description { get; set; } = string.Empty;
            public string WikipediaUrl { get; set; } = string.Empty;
            public string Website { get; set; } = string.Empty;
            public List<string> Images { get; set; } = new();
            public Dictionary<string, string> Specifications { get; set; } = new();
            public List<string> Features { get; set; } = new();
            public string Reviews_Summary { get; set; } = string.Empty;
            public List<string> Related_Products { get; set; } = new();
        }
    }
}
