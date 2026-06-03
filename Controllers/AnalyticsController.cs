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

            using (var connection = _context.CreateConnection())
            {
                // Get Total Products
                string countQuery = "SELECT COUNT(*) FROM T_PRODUCTS";
                using (var cmd = new SqlCommand(countQuery, (SqlConnection)connection))
                {
                    connection.Open();
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
            }

            ViewBag.TotalProducts = totalProducts;
            ViewBag.TotalCategories = totalCategories;
            
            // Format for Chart.js
            ViewBag.ChartLabels = string.Join(",", categoryStats.Keys.Select(k => $"'{k}'"));
            ViewBag.ChartData = string.Join(",", categoryStats.Values);

            return View();
        }
    }
}
