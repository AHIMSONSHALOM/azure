using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Http;
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

        private static readonly Dictionary<string, (string Url, string DefaultCategory)> Feeds = new()
        {
            { "TechCrunch", ("https://techcrunch.com/category/gadgets/feed/", "Gadgets") },
            { "Hacker News", ("https://news.ycombinator.com/rss", "Trending Tech") },
            { "Product Hunt", ("https://www.producthunt.com/feed", "Product Launch") }
        };

        public InternetDiscoveryService(ILogger<InternetDiscoveryService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("ProductHubSqlConnection")
                ?? throw new InvalidOperationException("Connection string not found.");

            // Create HttpClient with custom headers (specifically for Wikipedia API requirements)
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ProductHubDiscoveryService/1.0 (contact@producthub.local; educational purposes)");
        }

        public async Task<int> SyncLiveFeedsAsync()
        {
            _logger.LogInformation("Starting live RSS feeds sync...");
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
                    _logger.LogInformation($"Parsed {feedProducts.Count} products from {sourceName} RSS feed.");

                    foreach (var product in feedProducts)
                    {
                        // Check if product already exists in database
                        if (ProductExists(product.ProductName))
                        {
                            continue;
                        }

                        // Try to enrich product using Wikipedia API
                        _logger.LogInformation($"Enriching product '{product.ProductName}' with Wikipedia data...");
                        var (summary, wikiUrl, wikiImgUrl) = await FetchWikipediaDataAsync(product.ProductName);

                        if (!string.IsNullOrEmpty(summary))
                        {
                            product.AiSummary = summary;
                        }
                        if (!string.IsNullOrEmpty(wikiUrl))
                        {
                            product.WikipediaUrl = wikiUrl;
                        }
                        if (string.IsNullOrEmpty(product.ImageUrl) && !string.IsNullOrEmpty(wikiImgUrl))
                        {
                            product.ImageUrl = wikiImgUrl;
                        }

                        // Insert product into database
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

                    // Clean the product name
                    string productName = CleanProductName(rawTitle);
                    if (productName.Length > 255) productName = productName.Substring(0, 252) + "...";

                    // Parse LaunchDate
                    DateTime? launchDate = null;
                    if (DateTime.TryParse(pubDateStr, out DateTime parsedDate))
                    {
                        launchDate = parsedDate;
                    }

                    // Attempt to extract image URL
                    string? imageUrl = null;
                    
                    // Look for media:content or media:thumbnail
                    var mediaContent = item.Elements().FirstOrDefault(e => e.Name.LocalName == "content" && e.Name.NamespaceName.Contains("mrss"));
                    var mediaThumbnail = item.Elements().FirstOrDefault(e => e.Name.LocalName == "thumbnail" && e.Name.NamespaceName.Contains("mrss"));
                    
                    imageUrl = mediaContent?.Attribute("url")?.Value ?? mediaThumbnail?.Attribute("url")?.Value;

                    // Fallback: extract first img tag from description
                    if (string.IsNullOrEmpty(imageUrl) && !string.IsNullOrEmpty(description))
                    {
                        var match = Regex.Match(description, @"<img[^>]+src=""([^""]+)""");
                        if (match.Success)
                        {
                            imageUrl = match.Groups[1].Value;
                        }
                    }

                    // Remove HTML tags from description for storing
                    string cleanedDesc = StripHtmlTags(description);
                    if (cleanedDesc.Length > 1000)
                    {
                        cleanedDesc = cleanedDesc.Substring(0, 997) + "...";
                    }

                    // Look for a price mentioned in the title/description
                    double price = 0.0;
                    var priceMatch = Regex.Match(rawTitle + " " + cleanedDesc, @"\$([0-9]+(?:\.[0-9]{2})?)");
                    if (priceMatch.Success && double.TryParse(priceMatch.Groups[1].Value, out double parsedPrice))
                    {
                        price = parsedPrice;
                    }
                    else
                    {
                        // Assign a default price or keep it 0.0
                        price = 0.0;
                    }

                    // Determine Category
                    string category = defaultCategory;
                    var categoryElem = item.Element("category")?.Value;
                    if (!string.IsNullOrEmpty(categoryElem))
                    {
                        category = categoryElem;
                    }
                    if (category.Length > 100) category = category.Substring(0, 100);

                    // Rating
                    double rating = 4.0; // default rating for live discovered items
                    // Generate a semi-random rating based on word count of the description as a fun metric
                    int wordCount = cleanedDesc.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    rating = Math.Clamp(3.5 + (wordCount % 15) * 0.1, 3.5, 5.0);
                    rating = Math.Round(rating, 1);

                    // Host/domain of the source website
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
                        Category = category,
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

        private async Task<(string? Summary, string? WikipediaUrl, string? ImageUrl)> FetchWikipediaDataAsync(string productName)
        {
            try
            {
                string searchTitle = CleanTitleForSearch(productName);
                if (string.IsNullOrEmpty(searchTitle)) return (null, null, null);

                // 1. Search Wikipedia
                string searchUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(searchTitle)}&format=json&origin=*";
                var response = await _httpClient.GetAsync(searchUrl);
                if (!response.IsSuccessStatusCode) return (null, null, null);

                string searchJson = await response.Content.ReadAsStringAsync();
                using var searchDoc = JsonDocument.Parse(searchJson);
                
                if (searchDoc.RootElement.TryGetProperty("query", out var queryProp) &&
                    queryProp.TryGetProperty("search", out var searchList) &&
                    searchList.GetArrayLength() > 0)
                {
                    string wikiTitle = searchList[0].GetProperty("title").GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(wikiTitle))
                    {
                        // 2. Fetch page summary
                        string summaryUrl = $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(wikiTitle.Replace(" ", "_"))}";
                        var summaryResponse = await _httpClient.GetAsync(summaryUrl);
                        if (summaryResponse.IsSuccessStatusCode)
                        {
                            string summaryJson = await summaryResponse.Content.ReadAsStringAsync();
                            using var summaryDoc = JsonDocument.Parse(summaryJson);
                            var root = summaryDoc.RootElement;

                            string? summary = null;
                            if (root.TryGetProperty("extract", out var extractProp))
                            {
                                summary = extractProp.GetString();
                            }

                            string? wikiUrl = null;
                            if (root.TryGetProperty("content_urls", out var urlsProp) &&
                                urlsProp.TryGetProperty("desktop", out var desktopProp) &&
                                desktopProp.TryGetProperty("page", out var pageProp))
                            {
                                wikiUrl = pageProp.GetString();
                            }

                            string? wikiImageUrl = null;
                            if (root.TryGetProperty("thumbnail", out var thumbProp) &&
                                thumbProp.TryGetProperty("source", out var sourceProp))
                            {
                                wikiImageUrl = sourceProp.GetString();
                            }

                            return (summary, wikiUrl, wikiImageUrl);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Graceful failure
                _logger.LogWarning($"Wikipedia API query failed for '{productName}': {ex.Message}");
            }
            return (null, null, null);
        }

        private string CleanProductName(string rawTitle)
        {
            // Remove HN prefixes like "Show HN:"
            string name = rawTitle;
            var showHnRegex = new Regex(@"^(?:Show HN|Ask HN|Launch HN)\s*:\s*", RegexOptions.IgnoreCase);
            name = showHnRegex.Replace(name, "");
            
            // Remove TC review suffixes
            name = Regex.Replace(name, @"\s+review\s*$", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"^Review:\s*", "", RegexOptions.IgnoreCase);

            return name.Trim();
        }

        private string CleanTitleForSearch(string title)
        {
            // Strip special characters and extract first 3-4 words for high Wikipedia search matches
            string cleaned = Regex.Replace(title, @"[^\w\s\-\.]", "");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            
            var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length > 4 ? string.Join(" ", words.Take(4)) : cleaned;
        }

        private string StripHtmlTags(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            // Clean up standard tag closures
            string clean = Regex.Replace(html, "<.*?>", string.Empty);
            // Replace XML character entity references
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

        private bool InsertProduct(Product p)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                string query = @"
                    INSERT INTO T_PRODUCTS 
                    (F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_DESC, F_PROD_RATING, F_CATEGORY, F_LAUNCH_DATE, F_WEBSITE, F_AI_SUMMARY, F_WIKIPEDIA_URL, F_IMAGE_URL, F_ARTICLE_URL)
                    VALUES 
                    (@Name, @Brand, @Qty, @Price, @Desc, @Rating, @Category, @LaunchDate, @Website, @AiSummary, @WikipediaUrl, @ImageUrl, @ArticleUrl)";

                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Name", p.ProductName);
                cmd.Parameters.AddWithValue("@Brand", p.Brand);
                cmd.Parameters.AddWithValue("@Qty", p.Quantity);
                cmd.Parameters.AddWithValue("@Price", p.Price);
                cmd.Parameters.AddWithValue("@Desc", p.ProductDescription ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Rating", p.ProductRating);
                cmd.Parameters.AddWithValue("@Category", p.Category ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@LaunchDate", p.LaunchDate ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Website", p.Website ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@AiSummary", p.AiSummary ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@WikipediaUrl", p.WikipediaUrl ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@ImageUrl", p.ImageUrl ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@ArticleUrl", p.ArticleUrl ?? (object)DBNull.Value);

                conn.Open();
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to insert live product: '{p.ProductName}'");
                return false;
            }
        }
    }
}
