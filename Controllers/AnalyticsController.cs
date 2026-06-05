using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;
using ProductHub_MVC.Data;
using ProductHub_MVC.Models;

namespace ProductHub_MVC.Controllers
{
    public class AnalyticsController : Controller
    {
        private readonly SqlDbContext _context;

        public AnalyticsController(SqlDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            if (HttpContext.Session.GetString("UserSession") == null) return RedirectToAction("Login", "Account", new { area = "" });

            int totalProducts = 0;
            int totalCategories = 0;
            var categoryStats = new Dictionary<string, int>();

            int totalAiSearches = 0;
            int avgResponseTime = 0;
            var modelStats = new Dictionary<string, int>();
            var recentLogs = new List<AiSearchLogDto>();

            using (var connection = _context.CreateConnection())
            {
                connection.Open();

                // Get Total Products
                string countQuery = "SELECT COUNT(*) FROM T_PRODUCTS";
                using (var cmd = new SqlCommand(countQuery, (SqlConnection)connection))
                {
                    totalProducts = (int)cmd.ExecuteScalar();
                }

                // Get Categories Breakdown
                string catQuery = "SELECT ISNULL(F_CATEGORY, 'Uncategorized') as Cat, COUNT(*) as Cnt FROM T_PRODUCTS GROUP BY ISNULL(F_CATEGORY, 'Uncategorized')";
                using (var cmd = new SqlCommand(catQuery, (SqlConnection)connection))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            categoryStats[reader["Cat"].ToString() ?? "Unknown"] = (int)reader["Cnt"];
                            totalCategories++;
                        }
                    }
                }

                // Get Total AI Searches
                string aiCountQuery = "SELECT COUNT(*) FROM T_AI_SEARCH_LOGS";
                using (var cmd = new SqlCommand(aiCountQuery, (SqlConnection)connection))
                {
                    totalAiSearches = (int)cmd.ExecuteScalar();
                }

                // Get Avg Response Time
                string avgQuery = "SELECT COALESCE(AVG(F_RESPONSE_TIME_MS), 0) FROM T_AI_SEARCH_LOGS";
                using (var cmd = new SqlCommand(avgQuery, (SqlConnection)connection))
                {
                    avgResponseTime = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // Get Model Distribution
                string modelQuery = "SELECT F_MODEL_USED, COUNT(*) as Cnt FROM T_AI_SEARCH_LOGS GROUP BY F_MODEL_USED";
                using (var cmd = new SqlCommand(modelQuery, (SqlConnection)connection))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            modelStats[reader["F_MODEL_USED"].ToString() ?? "Unknown"] = (int)reader["Cnt"];
                        }
                    }
                }

                // Get Recent AI Logs
                string logsQuery = "SELECT TOP 10 F_SEARCH_QUERY, F_USER_SESSION, F_RESPONSE_TIME_MS, F_MODEL_USED, F_TIMESTAMP FROM T_AI_SEARCH_LOGS ORDER BY F_TIMESTAMP DESC";
                using (var cmd = new SqlCommand(logsQuery, (SqlConnection)connection))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            recentLogs.Add(new AiSearchLogDto
                            {
                                SearchQuery = reader["F_SEARCH_QUERY"].ToString() ?? "",
                                UserSession = reader["F_USER_SESSION"].ToString() ?? "",
                                ResponseTimeMs = Convert.ToInt32(reader["F_RESPONSE_TIME_MS"]),
                                ModelUsed = reader["F_MODEL_USED"].ToString() ?? "",
                                Timestamp = Convert.ToDateTime(reader["F_TIMESTAMP"])
                            });
                        }
                    }
                }
            }

            ViewBag.TotalProducts = totalProducts;
            ViewBag.TotalCategories = totalCategories;
            ViewBag.TotalAiSearches = totalAiSearches;
            ViewBag.AvgResponseTime = avgResponseTime;
            ViewBag.RecentLogs = recentLogs;
            
            // Format for Chart.js
            ViewBag.ChartLabels = string.Join(",", categoryStats.Keys.Select(k => $"'{k}'"));
            ViewBag.ChartData = string.Join(",", categoryStats.Values);

            ViewBag.ModelChartLabels = string.Join(",", modelStats.Keys.Select(k => $"'{k}'"));
            ViewBag.ModelChartData = string.Join(",", modelStats.Values);

            return View();
        }
    }

    public class AiSearchLogDto
    {
        public string SearchQuery { get; set; } = string.Empty;
        public string UserSession { get; set; } = string.Empty;
        public int ResponseTimeMs { get; set; }
        public string ModelUsed { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
