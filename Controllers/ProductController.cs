using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ProductHub_MVC.Data;
using ProductHub_MVC.Models;
using OfficeOpenXml;

using Microsoft.Extensions.Caching.Memory;

namespace ProductHub_MVC.Controllers
{
    public class ProductController : Controller
    {
        private readonly SqlDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly Services.InternetDiscoveryService _discoveryService;
        private readonly IMemoryCache _memoryCache;

        public ProductController(SqlDbContext context, IConfiguration configuration, Services.InternetDiscoveryService discoveryService, IMemoryCache memoryCache)
        {
            _context = context;
            _configuration = configuration;
            _discoveryService = discoveryService;
            _memoryCache = memoryCache;
        }

        // Centralized tracking helper engine to write audit logs smoothly
        private void LogActivity(string actionType, string description)
        {
            try {
                string user = HttpContext.Session.GetString("UserSession") ?? "SYSTEM";
                using (var conn = _context.CreateConnection()) {
                    string query = "INSERT INTO T_SYSTEM_HISTORY (F_USERNAME, F_ACTION_TYPE, F_DESCRIPTION) VALUES (@U, @A, @D)";
                    using (var cmd = new SqlCommand(query, (SqlConnection)conn)) {
                        cmd.Parameters.AddWithValue("@U", user);
                        cmd.Parameters.AddWithValue("@A", actionType);
                        cmd.Parameters.AddWithValue("@D", description);
                        conn.Open(); cmd.ExecuteNonQuery();
                    }
                }
            } catch { /* Fail-silent to guard thread execution speed */ }
        }

        private void GetOrCreateBrandAndCategoryIds(string brandName, string categoryName, out int? brandId, out int? categoryId)
        {
            brandId = null;
            categoryId = null;

            if (string.IsNullOrEmpty(brandName)) brandName = "Generic";
            if (string.IsNullOrEmpty(categoryName)) categoryName = "Electronics";

            try
            {
                using (var conn = _context.CreateConnection())
                {
                    conn.Open();

                    // 1. Resolve Brand
                    string checkBrand = "SELECT BrandId FROM T_BRANDS WHERE BrandName = @B";
                    using (var cmd = new SqlCommand(checkBrand, (SqlConnection)conn))
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
                            using (var insCmd = new SqlCommand(insertBrand, (SqlConnection)conn))
                            {
                                insCmd.Parameters.AddWithValue("@B", brandName);
                                brandId = Convert.ToInt32(insCmd.ExecuteScalar());
                            }
                        }
                    }

                    // 2. Resolve Category
                    string checkCat = "SELECT CategoryId FROM T_CATEGORIES WHERE CategoryName = @C";
                    using (var cmd = new SqlCommand(checkCat, (SqlConnection)conn))
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
                            using (var insCmd = new SqlCommand(insertCat, (SqlConnection)conn))
                            {
                                insCmd.Parameters.AddWithValue("@C", categoryName);
                                categoryId = Convert.ToInt32(insCmd.ExecuteScalar());
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // Helper method to save HTML email backups locally
        private void SaveEmailBackup(string subject, string recipient, string bodyHtml)
        {
            try
            {
                string backupDir = Path.Combine(Directory.GetCurrentDirectory(), "Sent_Mails_Backup");
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }
                string fileName = $"{Guid.NewGuid()}_{recipient.Replace("@", "_").Replace(".", "_")}.html";
                string filePath = Path.Combine(backupDir, fileName);
                string backupContent = $"<!--\nSubject: {subject}\nTo: {recipient}\nDate: {DateTime.Now}\n-->\n\n{bodyHtml}";
                System.IO.File.WriteAllText(filePath, backupContent);
            }
            catch { /* Fail-silent to guard thread execution speed */ }
        }

        private bool IsSessionValid()
        {
            // Session validation fully offloaded to the global SessionValidationFilter
            return true;
        }

        // =========================================================
        // 1. DATA GRID: FILTER, SEARCH & COLUMN SWITCHES ENGINE
        // =========================================================
        public IActionResult Index(string sortBy, string brandFilter, double? minPrice, double? maxPrice, double? minRating)
        {
            if (HttpContext.Session.GetString("UserSession") == null) return RedirectToAction("Login", "Account");
            if (!IsSessionValid())
            {
                HttpContext.Session.Clear();
                TempData["ErrorMessage"] = "⚠️ Session Terminated: Your account has been logged in on another machine.";
                return RedirectToAction("Login", "Account");
            }

            ViewBag.LoggedUser = HttpContext.Session.GetString("UserSession");
            ViewBag.IsAdmin = HttpContext.Session.GetInt32("IsAdmin") ?? 0;
            ViewBag.CanAddRow = HttpContext.Session.GetInt32("CanAddRow") ?? 0;
            ViewBag.CanDownload = HttpContext.Session.GetInt32("CanDownload") ?? 0;
            ViewBag.CanImport = HttpContext.Session.GetInt32("CanImport") ?? 0;
            ViewBag.CanExport = HttpContext.Session.GetInt32("CanExport") ?? 0;
            ViewBag.CanCompare = HttpContext.Session.GetInt32("CanCompare") ?? 0;
            ViewBag.CanEmail = HttpContext.Session.GetInt32("CanEmail") ?? 0;
            
            ViewBag.CanSeeBrand = HttpContext.Session.GetInt32("CanSeeBrand") ?? 0;
            ViewBag.CanSeeQty = HttpContext.Session.GetInt32("CanSeeQty") ?? 0;
            ViewBag.CanSeePrice = HttpContext.Session.GetInt32("CanSeePrice") ?? 0;
            ViewBag.CanSeeRating = HttpContext.Session.GetInt32("CanSeeRating") ?? 0;
            ViewBag.CanUseEdit = HttpContext.Session.GetInt32("CanUseEdit") ?? 0;
            ViewBag.CanUseDelete = HttpContext.Session.GetInt32("CanUseDelete") ?? 0;

            if (HttpContext.Session.GetString("PromptUpdateMobile") == "true")
            {
                ViewBag.PromptUpdateMobile = true;
            }

            // Read the dynamic corporate brand data isolation row config for this session profile
            string restrictedBrand = "ALL";
            using (var connection = _context.CreateConnection()) {
                string checkQuery = "SELECT F_RESTRICTED_BRAND FROM T_USERS WHERE F_USERNAME = @User";
                using (var cmd = new SqlCommand(checkQuery, (SqlConnection)connection)) {
                    cmd.Parameters.AddWithValue("@User", ViewBag.LoggedUser);
                    connection.Open();
                    var res = cmd.ExecuteScalar();
                    if (res != null) restrictedBrand = res.ToString();
                }
            }

            // Force query lock filter if root administrator has assigned a target brand isolation block
            if (restrictedBrand != "ALL") {
                brandFilter = restrictedBrand;
                ViewBag.ForcedIsolationNotice = $"🔒 Restricted Profile View: Data query locked exclusively onto brand data catalogs matching context logs: '{restrictedBrand}'.";
            }

            List<Product> products = FetchFilteredProducts(brandFilter, minPrice, maxPrice, minRating, sortBy);
            
            ViewBag.CurrentSort = sortBy;
            ViewBag.CurrentBrand = brandFilter;
            ViewBag.MinPrice = minPrice;
            ViewBag.MaxPrice = maxPrice;
            ViewBag.MinRating = minRating;

            return View(products);
        }

        // =========================================================================
        // 2. COMPARE SIDE-BY-SIDE GRID ACTION MATRIX ENGINE
        // =========================================================================
        [HttpGet]
        public IActionResult Compare(List<int> ids)
        {
            if (HttpContext.Session.GetString("UserSession") == null) return RedirectToAction("Login", "Account");
            if (!IsSessionValid())
            {
                HttpContext.Session.Clear();
                TempData["ErrorMessage"] = "⚠️ Session Terminated: Your account has been logged in on another machine.";
                return RedirectToAction("Login", "Account");
            }

            if (HttpContext.Session.GetInt32("CanCompare") != 1)
            {
                TempData["ErrorMessage"] = "🔒 Access Denied: The comparison engine module is disabled for your account profile.";
                return RedirectToAction(nameof(Index));
            }

            if (ids == null || ids.Count < 2 || ids.Count > 4)
            {
                TempData["ErrorMessage"] = "Boundary Notice: Please select between 2 and 4 products to compare side-by-side.";
                return RedirectToAction(nameof(Index));
            }

            // Log activity step into history table
            LogActivity("COMPARE", $"Loaded comparisons dashboard matrices side-by-side for {ids.Count} tracked products parameters.");

            List<Product> comparisonCollection = new List<Product>();
            using (var connection = _context.CreateConnection())
            {
                var parameterNames = string.Join(",", ids.Select((id, index) => $"@Id{index}"));
                string query = $"SELECT F_PRODUCT_ID, F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_DESC, F_PROD_RATING FROM T_PRODUCTS WHERE F_PRODUCT_ID IN ({parameterNames})";
                
                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    for (int i = 0; i < ids.Count; i++)
                    {
                        cmd.Parameters.AddWithValue($"@Id{i}", ids[i]);
                    }

                    connection.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            comparisonCollection.Add(new Product
                            {
                                ProductId = Convert.ToInt32(reader["F_PRODUCT_ID"]),
                                ProductName = reader["F_PROD_NAME"].ToString() ?? string.Empty,
                                Brand = reader["F_BRAND"].ToString() ?? string.Empty,
                                Quantity = reader["F_QTY"].ToString() ?? string.Empty,
                                Price = Convert.ToDouble(reader["F_PRICE"]),
                                ProductDescription = reader["F_PROD_DESC"]?.ToString() ?? "No specifications provided.",
                                ProductRating = Convert.ToDouble(reader["F_PROD_RATING"])
                            });
                        }
                    }
                }
            }

            return View(comparisonCollection);
        }

        // =========================================================================
        // 3. SEPARATE USERS ACCESS ROLES & DROPDOWN BRANDS LIFECYCLE CONTROLS
        // =========================================================================
        public IActionResult Users()
        {
            if (HttpContext.Session.GetString("UserSession") == null) return RedirectToAction("Login", "Account");
            if (!IsSessionValid())
            {
                HttpContext.Session.Clear();
                TempData["ErrorMessage"] = "⚠️ Session Terminated: Your account has been logged in on another machine.";
                return RedirectToAction("Login", "Account");
            }
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return RedirectToAction(nameof(Index));

            // Build dropdown source for "Show Brand": ALL + each brand with record count.
            Dictionary<string, int> availableBrandsWithCounts = new Dictionary<string, int>();
            int totalProductCount = 0;
            using (var connection = _context.CreateConnection()) {
                string brandListQuery = @"
                    SELECT F_BRAND, COUNT(*) AS ProductCount
                    FROM T_PRODUCTS
                    WHERE F_BRAND IS NOT NULL AND LTRIM(RTRIM(F_BRAND)) <> ''
                    GROUP BY F_BRAND
                    ORDER BY F_BRAND ASC";
                using (var cmd = new SqlCommand(brandListQuery, (SqlConnection)connection)) {
                    connection.Open();
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            string brand = reader["F_BRAND"]?.ToString()?.Trim() ?? string.Empty;
                            int brandCount = Convert.ToInt32(reader["ProductCount"]);
                            if (string.IsNullOrWhiteSpace(brand)) continue;

                            // Guard against duplicate keys from spacing/casing anomalies.
                            if (!availableBrandsWithCounts.ContainsKey(brand)) {
                                availableBrandsWithCounts[brand] = brandCount;
                            } else {
                                availableBrandsWithCounts[brand] += brandCount;
                            }
                            totalProductCount += brandCount;
                        }
                    }
                }
            }

            availableBrandsWithCounts = availableBrandsWithCounts
                .OrderBy(kvp => kvp.Key)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            availableBrandsWithCounts["ALL"] = totalProductCount;
            availableBrandsWithCounts = availableBrandsWithCounts
                .OrderByDescending(kvp => kvp.Key == "ALL")
                .ThenBy(kvp => kvp.Key)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            ViewBag.AvailableBrandsWithCounts = availableBrandsWithCounts;

            List<Dictionary<string, object>> userProfiles = new List<Dictionary<string, object>>();
            using (var connection = _context.CreateConnection())
            {
                string query = "SELECT F_USER_ID, F_USERNAME, F_PASSWORD, F_MOBILE_NUMBER, F_BACKUP_CODE, F_RESTRICTED_BRAND, F_IS_ADMIN, F_CAN_ADD_ROW, F_CAN_DOWNLOAD, F_CAN_IMPORT, F_CAN_EXPORT, F_CAN_COMPARE, F_CAN_EMAIL, F_CAN_SEE_BRAND, F_CAN_SEE_QTY, F_CAN_SEE_PRICE, F_CAN_SEE_RATING, F_CAN_USE_EDIT, F_CAN_USE_DELETE, F_EMAIL, F_IS_APPROVED FROM T_USERS";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    connection.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) userProfiles.Add(new Dictionary<string, object> {
                            { "Id", reader["F_USER_ID"] }, { "Name", reader["F_USERNAME"].ToString() ?? "" },
                            { "Password", reader["F_PASSWORD"].ToString() ?? "" }, { "Mobile", reader["F_MOBILE_NUMBER"].ToString() ?? "" },
                            { "BackupCode", reader["F_BACKUP_CODE"].ToString() ?? "" }, 
                            { "RestrictedBrand", reader["F_RESTRICTED_BRAND"].ToString() ?? "ALL" }, 
                            { "IsAdmin", Convert.ToInt32(reader["F_IS_ADMIN"]) },
                            { "AddRow", Convert.ToInt32(reader["F_CAN_ADD_ROW"]) }, { "Download", Convert.ToInt32(reader["F_CAN_DOWNLOAD"]) },
                            { "Import", Convert.ToInt32(reader["F_CAN_IMPORT"]) }, { "Export", Convert.ToInt32(reader["F_CAN_EXPORT"]) },
                            { "Compare", Convert.ToInt32(reader["F_CAN_COMPARE"]) }, { "Email", Convert.ToInt32(reader["F_CAN_EMAIL"]) },
                            { "SeeBrand", Convert.ToInt32(reader["F_CAN_SEE_BRAND"]) }, { "SeeQty", Convert.ToInt32(reader["F_CAN_SEE_QTY"]) },
                            { "SeePrice", Convert.ToInt32(reader["F_CAN_SEE_PRICE"]) }, { "SeeRating", Convert.ToInt32(reader["F_CAN_SEE_RATING"]) },
                            { "UseEdit", Convert.ToInt32(reader["F_CAN_USE_EDIT"]) }, { "UseDelete", Convert.ToInt32(reader["F_CAN_USE_DELETE"]) },
                            { "EmailAddress", reader["F_EMAIL"]?.ToString() ?? "" }, { "IsApproved", Convert.ToInt32(reader["F_IS_APPROVED"]) }
                        });
                    }
                }
            }
            return View(userProfiles);
        }

        [HttpPost]
        public IActionResult SaveUserConfig(int userId, string userNameParam, string restrictedBrand, int addRow, int download, int import, int export, int compare, int email,
                                            int seeBrand, int seeQty, int seePrice, int seeRating, int useEdit, int useDelete, string userEmail)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();
            using (var connection = _context.CreateConnection()) {
                string query = @"UPDATE T_USERS SET 
                                F_CAN_ADD_ROW=@Add, F_CAN_DOWNLOAD=@Dl, F_CAN_IMPORT=@Imp, F_CAN_EXPORT=@Exp, F_CAN_COMPARE=@Comp, F_CAN_EMAIL=@Em,
                                F_CAN_SEE_BRAND=@B, F_CAN_SEE_QTY=@Q, F_CAN_SEE_PRICE=@P, F_CAN_SEE_RATING=@R, F_CAN_USE_EDIT=@E, F_CAN_USE_DELETE=@D,
                                F_RESTRICTED_BRAND=@Restrict, F_EMAIL=@Email
                                WHERE F_USER_ID=@Id";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection)) {
                    cmd.Parameters.AddWithValue("@Id", userId); cmd.Parameters.AddWithValue("@Add", addRow);
                    cmd.Parameters.AddWithValue("@Dl", download); cmd.Parameters.AddWithValue("@Imp", import);
                    cmd.Parameters.AddWithValue("@Exp", export); cmd.Parameters.AddWithValue("@Comp", compare);
                    cmd.Parameters.AddWithValue("@Em", email);
                    cmd.Parameters.AddWithValue("@B", seeBrand); cmd.Parameters.AddWithValue("@Q", seeQty);
                    cmd.Parameters.AddWithValue("@P", seePrice); cmd.Parameters.AddWithValue("@R", seeRating);
                    cmd.Parameters.AddWithValue("@E", useEdit); cmd.Parameters.AddWithValue("@D", useDelete);
                    cmd.Parameters.AddWithValue("@Restrict", restrictedBrand.Trim()); 
                    cmd.Parameters.AddWithValue("@Email", (object)userEmail?.Trim() ?? DBNull.Value);
                    connection.Open(); cmd.ExecuteNonQuery();
                }
            }
            LogActivity("PERMISSIONS", $"Modified authorization switches and structural visibility brand constraint to '{restrictedBrand}' for account user: '{userNameParam}'.");
            TempData["SuccessMessage"] = "🛡️ Permission configuration synchronized live in real-time!";
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        public IActionResult ApproveUser(int userId, string targetName)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();
            
            string userEmail = "";
            string userMobile = "";
            string autoPassword = "";
            
            // Auto-generate fresh simple password upon approval to guarantee they receive it
            const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            Random random = new Random();
            autoPassword = new string(Enumerable.Repeat(validChars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());

            using (var connection = _context.CreateConnection())
            {
                connection.Open();
                
                // 1. Retrieve the registered email address and mobile number of the user
                string userQuery = "SELECT F_EMAIL, F_MOBILE_NUMBER FROM T_USERS WHERE F_USER_ID = @Id";
                using (var cmd = new SqlCommand(userQuery, (SqlConnection)connection))
                {
                    cmd.Parameters.AddWithValue("@Id", userId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            userEmail = reader["F_EMAIL"]?.ToString() ?? "";
                            userMobile = reader["F_MOBILE_NUMBER"]?.ToString() ?? "";
                        }
                    }
                }

                // 2. Approve the user account AND update their password
                string query = "UPDATE T_USERS SET F_IS_APPROVED = 1, F_PASSWORD = @Pass WHERE F_USER_ID = @Id";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    cmd.Parameters.AddWithValue("@Id", userId);
                    cmd.Parameters.AddWithValue("@Pass", autoPassword);
                    cmd.ExecuteNonQuery();
                }
            }

            // 3. Dispatch security notification email asynchronously/synchronously in background (prevents blocking admin console thread)
            if (!string.IsNullOrWhiteSpace(userEmail))
            {
                string scheme = HttpContext.Request.Scheme;
                string host = HttpContext.Request.Host.ToUriComponent();
                string baseUrl = $"{scheme}://{host}";

                _ = Task.Run(() => {
                    try
                    {
                        string senderEmail = _configuration["EmailSettings:Username"]; 
                        string senderPassword = _configuration["EmailSettings:Password"]; 
                        using (MailMessage mail = new MailMessage()) { 
                            mail.From = new MailAddress(senderEmail, "ProductHub Admin"); 
                            mail.To.Add(userEmail.Trim()); 
                            mail.Subject = "🎉 Account Approved - ProductHub Access Granted"; 
                            mail.IsBodyHtml = true;
                            mail.Body = $@"
                                <div style='font-family: &quot;Segoe UI&quot;, Tahoma, Geneva, Verdana, sans-serif; max-width: 600px; margin: auto; padding: 30px; border: 1px solid #e2e8f0; border-radius: 16px; background-color: #ffffff; box-shadow: 0 4px 12px rgba(0,0,0,0.02);'>
                                    <div style='text-align: center; margin-bottom: 24px;'>
                                        <div style='background-color: #E8F5E9; color: #16A34A; display: inline-block; padding: 12px; border-radius: 50%; margin-bottom: 12px;'>
                                            <svg width='32' height='32' fill='currentColor' viewBox='0 0 24 24' style='display: block;'>
                                                <path d='M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z'/>
                                            </svg>
                                        </div>
                                        <h2 style='color: #1E293B; margin: 0 0 8px 0; font-weight: 700; font-size: 22px;'>Access Granted!</h2>
                                        <p style='color: #64748B; font-size: 14px; margin: 0;'>Your ProductHub account has been officially approved.</p>
                                    </div>
                                    <div style='margin-bottom: 24px; line-height: 1.6; color: #334155; font-size: 15px;'>
                                        <p>Hello <strong>{targetName}</strong>,</p>
                                        <p>Great news! The system administrator has reviewed and <strong>approved</strong> your registration request. You can now log into ProductHub using your registered Google account or the credentials below:</p>
                                        
                                        <div style='background-color: #F8FAFC; border: 1px solid #E2E8F0; border-radius: 8px; padding: 18px; margin: 20px 0;'>
                                            <table style='width: 100%; border-collapse: collapse; font-size: 14.5px;'>
                                                <tr>
                                                    <td style='padding: 6px 0; color: #64748B; width: 140px; font-weight: 500;'>Username:</td>
                                                    <td style='padding: 6px 0; color: #1E293B; font-weight: 700;'>{targetName}</td>
                                                </tr>
                                                <tr>
                                                    <td style='padding: 6px 0; color: #64748B; font-weight: 500;'>Generated Pass:</td>
                                                    <td style='padding: 6px 0; color: #EF4444; font-weight: 700; font-family: monospace; font-size: 15.5px;'>{autoPassword}</td>
                                                </tr>
                                                <tr>
                                                    <td style='padding: 6px 0; color: #64748B; font-weight: 500;'>Mobile Number:</td>
                                                    <td style='padding: 6px 0; color: #1E293B; font-weight: 600;'>{userMobile}</td>
                                                </tr>
                                            </table>
                                        </div>
                                        
                                        <div style='background-color: #FFFBEB; padding: 20px; border-left: 4px solid #D97706; margin: 24px 0; border-radius: 0 8px 8px 0;'>
                                            <p style='margin: 0; font-weight: 700; color: #B45309; font-size: 14.2px;'>⚠️ Action Required:</p>
                                            <p style='margin: 6px 0 0 0; color: #78350F; font-size: 13.5px;'>Please sign in and ensure that your profile information is up to date, including your <strong>mobile number</strong>, to secure your account and configure 2-Step OTP options.</p>
                                        </div>
                                    </div>
                                    <div style='text-align: center; margin-top: 30px;'>
                                        <a href='{baseUrl}' style='background-color: #16A34A; color: #ffffff; padding: 12px 28px; text-decoration: none; border-radius: 8px; font-weight: 600; display: inline-block; box-shadow: 0 2px 4px rgba(22,163,74,0.15); transition: background-color 0.15s ease;'>Log into ProductHub</a>
                                    </div>
                                    <hr style='border: 0; border-top: 1px solid #e2e8f0; margin: 30px 0;' />
                                    <div style='text-align: center; font-size: 12px; color: #94A3B8;'>
                                        This is a secure security notification from ProductHub.<br/>
                                        If you did not request this account activation, please contact system support.
                                    </div>
                                </div>"; 
                            
                            using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587)) { 
                                smtp.EnableSsl = true; 
                                smtp.UseDefaultCredentials = false; 
                                smtp.Credentials = new NetworkCredential(senderEmail, senderPassword); 
                                smtp.Send(mail); 
                            } 
                            SaveEmailBackup(mail.Subject, userEmail, mail.Body);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"SMTP Dispatch Error: {ex.Message}");
                    }
                });
            }

            LogActivity("USER_APPROVAL", $"Approved pending registration access request and activated dashboard permissions for user: '{targetName}'.");
            TempData["SuccessMessage"] = $"✅ User '{targetName}' has been successfully approved and granted application access!";
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        public IActionResult RejectUser(int userId, string targetName)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();
            using (var connection = _context.CreateConnection())
            {
                string query = "DELETE FROM T_USERS WHERE F_USER_ID = @Id AND F_IS_APPROVED = 0 AND F_IS_ADMIN = 0";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    cmd.Parameters.AddWithValue("@Id", userId);
                    connection.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            LogActivity("USER_REJECTION", $"Rejected and deleted pending registration request for user: '{targetName}'.");
            TempData["SuccessMessage"] = $"❌ Registration request for user '{targetName}' has been rejected and removed.";
            return RedirectToAction(nameof(Users));
        }

        // =========================================================================
        // 4. CENTRALIZED HISTORY TRANSACTION SYSTEM VISUALIZER
        // =========================================================================
        [HttpGet]
        public IActionResult History(string targetUser)
        {
            if (HttpContext.Session.GetString("UserSession") == null) return RedirectToAction("Login", "Account");
            if (!IsSessionValid())
            {
                HttpContext.Session.Clear();
                TempData["ErrorMessage"] = "⚠️ Session Terminated: Your account has been logged in on another machine.";
                return RedirectToAction("Login", "Account");
            }
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();

            ViewBag.LoggedUser = HttpContext.Session.GetString("UserSession");
            ViewBag.IsAdmin = HttpContext.Session.GetInt32("IsAdmin") ?? 0;
            ViewBag.SelectedFilterUser = targetUser;

            List<Dictionary<string, object>> auditLogsCollection = new List<Dictionary<string, object>>();
            using (var connection = _context.CreateConnection())
            {
                string query = "SELECT F_LOG_ID, F_USERNAME, F_ACTION_TYPE, F_DESCRIPTION, F_TIMESTAMP FROM T_SYSTEM_HISTORY ";
                if (!string.IsNullOrEmpty(targetUser)) query += " WHERE F_USERNAME = @TgtUser ";
                query += " ORDER BY F_TIMESTAMP DESC";

                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    if (!string.IsNullOrEmpty(targetUser)) cmd.Parameters.AddWithValue("@TgtUser", targetUser.Trim());
                    connection.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) {
                            auditLogsCollection.Add(new Dictionary<string, object>{
                                { "Id", reader["F_LOG_ID"] }, { "User", reader["F_USERNAME"].ToString() ?? "" },
                                { "Action", reader["F_ACTION_TYPE"].ToString() ?? "" }, { "Desc", reader["F_DESCRIPTION"].ToString() ?? "" },
                                { "Time", Convert.ToDateTime(reader["F_TIMESTAMP"]).ToString("dd MMM yyyy, hh:mm tt") }
                            });
                        }
                    }
                }
            }
            return View(auditLogsCollection);
        }

        // =========================================================
        // 5. INVENTORY CRUD LOGISTICS HANDLERS
        [HttpGet]
        public IActionResult Details(int id)
        {
            if (HttpContext.Session.GetString("UserSession") == null) return RedirectToAction("Login", "Account");
            if (!IsSessionValid())
            {
                HttpContext.Session.Clear();
                TempData["ErrorMessage"] = "⚠️ Session Terminated: Your account has been logged in on another machine.";
                return RedirectToAction("Login", "Account");
            }

            Product product = null;
            using (var connection = _context.CreateConnection()) {
                string query = "SELECT F_PRODUCT_ID, F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_DESC, F_PROD_RATING, F_CATEGORY, F_SUBCATEGORY, F_LAUNCH_DATE, F_WEBSITE, F_AI_SUMMARY, F_WIKIPEDIA_URL, F_IMAGE_URL, F_ARTICLE_URL, F_IS_APPROVED FROM T_PRODUCTS WHERE F_PRODUCT_ID = @Id";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection)) {
                    cmd.Parameters.AddWithValue("@Id", id);
                    connection.Open();
                    using (var reader = cmd.ExecuteReader()) {
                        if (reader.Read()) {
                            product = new Product {
                                ProductId = Convert.ToInt32(reader["F_PRODUCT_ID"]),
                                ProductName = reader["F_PROD_NAME"].ToString() ?? "",
                                Brand = reader["F_BRAND"].ToString() ?? "",
                                Quantity = reader["F_QTY"].ToString() ?? "",
                                Price = Convert.ToDouble(reader["F_PRICE"] == DBNull.Value ? 0 : reader["F_PRICE"]),
                                ProductDescription = reader["F_PROD_DESC"]?.ToString(),
                                ProductRating = Convert.ToDouble(reader["F_PROD_RATING"] == DBNull.Value ? 0 : reader["F_PROD_RATING"]),
                                Category = reader["F_CATEGORY"]?.ToString(),
                                Subcategory = reader["F_SUBCATEGORY"]?.ToString(),
                                LaunchDate = reader["F_LAUNCH_DATE"] != DBNull.Value ? Convert.ToDateTime(reader["F_LAUNCH_DATE"]) : (DateTime?)null,
                                Website = reader["F_WEBSITE"]?.ToString(),
                                AiSummary = reader["F_AI_SUMMARY"]?.ToString(),
                                WikipediaUrl = reader["F_WIKIPEDIA_URL"]?.ToString(),
                                ImageUrl = reader["F_IMAGE_URL"]?.ToString(),
                                ArticleUrl = reader["F_ARTICLE_URL"]?.ToString(),
                                IsApproved = reader["F_IS_APPROVED"] == DBNull.Value ? true : Convert.ToBoolean(reader["F_IS_APPROVED"])
                            };
                        }
                    }
                }
            }
            
            if (product == null) return NotFound();

            // Load images from database
            using (var connection = _context.CreateConnection()) {
                string query = "SELECT ImageId, ProductId, ImageUrl, Source, SourceUrl, IsPrimary, CreatedAt FROM T_PRODUCT_IMAGES WHERE ProductId = @Id";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection)) {
                    cmd.Parameters.AddWithValue("@Id", id);
                    connection.Open();
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            product.ProductImages.Add(new ProductImage {
                                ImageId = Convert.ToInt32(reader["ImageId"]),
                                ProductId = Convert.ToInt32(reader["ProductId"]),
                                ImageUrl = reader["ImageUrl"].ToString() ?? "",
                                Source = reader["Source"]?.ToString(),
                                SourceUrl = reader["SourceUrl"]?.ToString(),
                                IsPrimary = Convert.ToBoolean(reader["IsPrimary"]),
                                CreatedAt = Convert.ToDateTime(reader["CreatedAt"])
                            });
                        }
                    }
                }
            }

            // Load sources from database
            using (var connection = _context.CreateConnection()) {
                string query = "SELECT SourceId, ProductId, SourceUrl, SourceName FROM T_PRODUCT_SOURCES WHERE ProductId = @Id";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection)) {
                    cmd.Parameters.AddWithValue("@Id", id);
                    connection.Open();
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            product.ProductSources.Add(new ProductSource {
                                SourceId = Convert.ToInt32(reader["SourceId"]),
                                ProductId = Convert.ToInt32(reader["ProductId"]),
                                SourceUrl = reader["SourceUrl"].ToString() ?? "",
                                SourceName = reader["SourceName"].ToString() ?? ""
                            });
                        }
                    }
                }
            }

            // Load news from database
            using (var connection = _context.CreateConnection()) {
                string query = "SELECT NewsId, ProductId, Title, Url, Summary, COALESCE(PublishedDate, PublishDate) AS PublishedDate FROM T_PRODUCT_NEWS WHERE ProductId = @Id";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection)) {
                    cmd.Parameters.AddWithValue("@Id", id);
                    connection.Open();
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            product.ProductNews.Add(new ProductNews {
                                NewsId = Convert.ToInt32(reader["NewsId"]),
                                ProductId = Convert.ToInt32(reader["ProductId"]),
                                Title = reader["Title"].ToString() ?? "",
                                Url = reader["Url"].ToString() ?? "",
                                Summary = reader["Summary"]?.ToString(),
                                PublishedDate = reader["PublishedDate"] != DBNull.Value ? Convert.ToDateTime(reader["PublishedDate"]) : (DateTime?)null
                            });
                        }
                    }
                }
            }

            // Increment views in T_ANALYTICS
            try {
                using (var connection = _context.CreateConnection()) {
                    string countQuery = @"
                        IF EXISTS (SELECT 1 FROM T_ANALYTICS WHERE F_PRODUCT_ID=@Id)
                            UPDATE T_ANALYTICS SET F_VIEW_COUNT=F_VIEW_COUNT+1, F_LAST_VIEWED=GETDATE() WHERE F_PRODUCT_ID=@Id
                        ELSE
                            INSERT INTO T_ANALYTICS (F_PRODUCT_ID, F_VIEW_COUNT, F_LAST_VIEWED) VALUES (@Id, 1, GETDATE())";
                    using (var cmd = new SqlCommand(countQuery, (SqlConnection)connection)) {
                        cmd.Parameters.AddWithValue("@Id", id);
                        connection.Open();
                        cmd.ExecuteNonQuery();
                    }
                }
            } catch { }

            // On-Demand Enrichment: Trigger if details are incomplete or images list is empty
            if (string.IsNullOrEmpty(product.ProductDescription) || 
                product.ProductDescription == "No description available." || 
                product.ProductDescription == "No specifications provided." || 
                product.ProductImages.Count == 0)
            {
                _ = Task.Run(() => TriggerOnDemandEnrichmentAsync(product));
            }
            
            LogActivity("VIEW_DETAILS", $"Viewed detailed AI profile for product: '{product.ProductName}'.");
            return View(product);
        }

        private async Task<bool> TriggerOnDemandEnrichmentAsync(Product p)
        {
            try
            {
                var enrichment = await _discoveryService.FetchEnrichmentAsync(p.ProductName, p.Brand);
                if (enrichment == null) return false;

                using (var connection = _context.CreateConnection())
                {
                    connection.Open();
                    
                    string descJson = JsonSerializer.Serialize(new {
                        description = enrichment.Description,
                        specifications = enrichment.Specifications,
                        features = enrichment.Features
                    });
                    string updateQuery = "UPDATE T_PRODUCTS SET F_PROD_DESC=@Desc, F_WEBSITE=@Website, F_WIKIPEDIA_URL=@WikiUrl, F_IMAGE_URL=@ImgUrl WHERE F_PRODUCT_ID=@Id";
                    using (var cmd = new SqlCommand(updateQuery, (SqlConnection)connection))
                    {
                        cmd.Parameters.AddWithValue("@Desc", descJson);
                        cmd.Parameters.AddWithValue("@Website", enrichment.Website);
                        cmd.Parameters.AddWithValue("@WikiUrl", enrichment.WikipediaUrl);
                        cmd.Parameters.AddWithValue("@ImgUrl", enrichment.Images.FirstOrDefault() ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Id", p.ProductId);
                        cmd.ExecuteNonQuery();
                    }

                    if (enrichment.Images != null && enrichment.Images.Count > 0)
                    {
                        string clearQuery = "DELETE FROM T_PRODUCT_IMAGES WHERE ProductId=@Id";
                        using (var clearCmd = new SqlCommand(clearQuery, (SqlConnection)connection))
                        {
                            clearCmd.Parameters.AddWithValue("@Id", p.ProductId);
                            clearCmd.ExecuteNonQuery();
                        }

                        string imgQuery = "INSERT INTO T_PRODUCT_IMAGES (ProductId, ImageUrl, Source, IsPrimary) VALUES (@ProductId, @ImageUrl, @Source, @IsPrimary)";
                        bool first = true;
                        foreach (var img in enrichment.Images)
                        {
                            using (var imgCmd = new SqlCommand(imgQuery, (SqlConnection)connection))
                            {
                                imgCmd.Parameters.AddWithValue("@ProductId", p.ProductId);
                                imgCmd.Parameters.AddWithValue("@ImageUrl", img);
                                imgCmd.Parameters.AddWithValue("@Source", "AI Scraper");
                                imgCmd.Parameters.AddWithValue("@IsPrimary", first);
                                imgCmd.ExecuteNonQuery();
                            }
                            first = false;
                        }
                    }

                    string checkSrc = "SELECT COUNT(1) FROM T_PRODUCT_SOURCES WHERE ProductId=@Id";
                    int srcCount = 0;
                    using (var checkCmd = new SqlCommand(checkSrc, (SqlConnection)connection))
                    {
                        checkCmd.Parameters.AddWithValue("@Id", p.ProductId);
                        srcCount = (int)checkCmd.ExecuteScalar();
                    }

                    if (srcCount == 0)
                    {
                        string srcQuery = "INSERT INTO T_PRODUCT_SOURCES (ProductId, SourceUrl, SourceName) VALUES (@ProductId, @SourceUrl, @SourceName)";
                        if (!string.IsNullOrEmpty(enrichment.WikipediaUrl))
                        {
                            using var cmd = new SqlCommand(srcQuery, (SqlConnection)connection);
                            cmd.Parameters.AddWithValue("@ProductId", p.ProductId);
                            cmd.Parameters.AddWithValue("@SourceUrl", enrichment.WikipediaUrl);
                            cmd.Parameters.AddWithValue("@SourceName", "Wikipedia");
                            cmd.ExecuteNonQuery();
                        }
                        if (!string.IsNullOrEmpty(enrichment.Website))
                        {
                            using var cmd = new SqlCommand(srcQuery, (SqlConnection)connection);
                            cmd.Parameters.AddWithValue("@ProductId", p.ProductId);
                            cmd.Parameters.AddWithValue("@SourceUrl", enrichment.Website.StartsWith("http") ? enrichment.Website : $"https://{enrichment.Website}");
                            cmd.Parameters.AddWithValue("@SourceName", "Official Website");
                            cmd.ExecuteNonQuery();
                        }
                        if (!string.IsNullOrEmpty(p.ArticleUrl))
                        {
                            using var cmd = new SqlCommand(srcQuery, (SqlConnection)connection);
                            cmd.Parameters.AddWithValue("@ProductId", p.ProductId);
                            cmd.Parameters.AddWithValue("@SourceUrl", p.ArticleUrl);
                            cmd.Parameters.AddWithValue("@SourceName", p.Brand);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LogActivity("ENRICH_ERROR", $"On-demand enrichment failed for {p.ProductId}: {ex.Message}");
                return false;
            }
        }

        private async Task TriggerBackgroundEnrichmentForProductId(int id, string name, string brand)
        {
            try
            {
                var enrichment = await _discoveryService.FetchEnrichmentAsync(name, brand);
                if (enrichment == null) return;
                
                using (var conn = _context.CreateConnection())
                {
                    conn.Open();
                    string descJson = JsonSerializer.Serialize(new {
                        description = enrichment.Description,
                        specifications = enrichment.Specifications,
                        features = enrichment.Features
                    });
                    string q = "UPDATE T_PRODUCTS SET F_PROD_DESC=@Desc, F_WEBSITE=@Website, F_WIKIPEDIA_URL=@WikiUrl, F_IMAGE_URL=@ImgUrl WHERE F_PRODUCT_ID=@Id";
                    using (var cmd = new SqlCommand(q, (SqlConnection)conn)) {
                        cmd.Parameters.AddWithValue("@Desc", descJson);
                        cmd.Parameters.AddWithValue("@Website", enrichment.Website);
                        cmd.Parameters.AddWithValue("@WikiUrl", enrichment.WikipediaUrl);
                        cmd.Parameters.AddWithValue("@ImgUrl", enrichment.Images.FirstOrDefault() ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Id", id);
                        cmd.ExecuteNonQuery();
                    }

                    if (enrichment.Images != null && enrichment.Images.Count > 0)
                    {
                        string imgQuery = "INSERT INTO T_PRODUCT_IMAGES (ProductId, ImageUrl, Source, IsPrimary) VALUES (@ProductId, @ImageUrl, @Source, @IsPrimary)";
                        bool first = true;
                        foreach (var img in enrichment.Images)
                        {
                            using (var imgCmd = new SqlCommand(imgQuery, (SqlConnection)conn))
                            {
                                imgCmd.Parameters.AddWithValue("@ProductId", id);
                                imgCmd.Parameters.AddWithValue("@ImageUrl", img);
                                imgCmd.Parameters.AddWithValue("@Source", "AI Scraper");
                                imgCmd.Parameters.AddWithValue("@IsPrimary", first);
                                imgCmd.ExecuteNonQuery();
                            }
                            first = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogActivity("BG_ENRICH_ERROR", $"Background enrichment failed for Product {id}: {ex.Message}");
            }
        }

        [HttpPost]
        public IActionResult AddProduct(Product m)
        {
            int newId = 0;
            GetOrCreateBrandAndCategoryIds(m.Brand, m.Category, out int? brandId, out int? categoryId);
            using (var c = _context.CreateConnection()) {
                string q = "INSERT INTO T_PRODUCTS (F_PROD_NAME,F_BRAND,F_BRAND_ID,F_QTY,F_PRICE,F_PROD_RATING,F_CATEGORY,F_CATEGORY_ID,F_SUBCATEGORY,F_IS_APPROVED) VALUES (@N,@B,@BrandId,@Q,@P,@R,@Category,@CategoryId,@Subcategory,1); SELECT SCOPE_IDENTITY();";
                using (var cmd = new SqlCommand(q, (SqlConnection)c)) {
                    cmd.Parameters.AddWithValue("@N", m.ProductName);
                    cmd.Parameters.AddWithValue("@B", m.Brand);
                    cmd.Parameters.AddWithValue("@BrandId", brandId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Q", m.Quantity);
                    cmd.Parameters.AddWithValue("@P", m.Price);
                    cmd.Parameters.AddWithValue("@R", m.ProductRating);
                    cmd.Parameters.AddWithValue("@Category", m.Category ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@CategoryId", categoryId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Subcategory", m.Subcategory ?? (object)DBNull.Value);
                    c.Open();
                    newId = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }

            // Phase 1 Requirement: Fetch multiple images and details when product is added
            Task.Run(() => TriggerBackgroundEnrichmentForProductId(newId, m.ProductName, m.Brand));

            LogActivity("ADD_ROW", $"Inserted brand-new inventory data row item: '{m.ProductName}' priced at Rs. {m.Price}.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult EditProduct(Product m)
        {
            GetOrCreateBrandAndCategoryIds(m.Brand, m.Category, out int? brandId, out int? categoryId);
            using (var c = _context.CreateConnection()) {
                string q = "UPDATE T_PRODUCTS SET F_PROD_NAME=@N,F_BRAND=@B,F_BRAND_ID=@BrandId,F_QTY=@Q,F_PRICE=@P,F_PROD_RATING=@R,F_CATEGORY=@Category,F_CATEGORY_ID=@CategoryId,F_SUBCATEGORY=@Subcategory WHERE F_PRODUCT_ID=@I";
                using (var cmd = new SqlCommand(q, (SqlConnection)c)) {
                    cmd.Parameters.AddWithValue("@I", m.ProductId);
                    cmd.Parameters.AddWithValue("@N", m.ProductName);
                    cmd.Parameters.AddWithValue("@B", m.Brand);
                    cmd.Parameters.AddWithValue("@BrandId", brandId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Q", m.Quantity);
                    cmd.Parameters.AddWithValue("@P", m.Price);
                    cmd.Parameters.AddWithValue("@R", m.ProductRating);
                    cmd.Parameters.AddWithValue("@Category", m.Category ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@CategoryId", categoryId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Subcategory", m.Subcategory ?? (object)DBNull.Value);
                    c.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            LogActivity("EDIT", $"Updated inventory data specification parameters for component: '{m.ProductName}'.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult DeleteProduct(int id)
        {
            string namePlaceholder = $"ID {id}";
            using (var c = _context.CreateConnection()) {
                c.Open();
                using (var getNameCmd = new SqlCommand("SELECT F_PROD_NAME FROM T_PRODUCTS WHERE F_PRODUCT_ID=@I", (SqlConnection)c)) {
                    getNameCmd.Parameters.AddWithValue("@I", id);
                    namePlaceholder = getNameCmd.ExecuteScalar()?.ToString() ?? namePlaceholder;
                }
                using (var cmd = new SqlCommand("DELETE FROM T_PRODUCTS WHERE F_PRODUCT_ID=@I", (SqlConnection)c)) {
                    cmd.Parameters.AddWithValue("@I", id);
                    cmd.ExecuteNonQuery();
                }
            }
            LogActivity("DELETE", $"Removed item row permanently from product catalog: '{namePlaceholder}'.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> SyncLiveFeeds()
        {
            if (HttpContext.Session.GetString("UserSession") == null) return RedirectToAction("Login", "Account");
            if (!IsSessionValid())
            {
                HttpContext.Session.Clear();
                TempData["ErrorMessage"] = "⚠️ Session Terminated: Your account has been logged in on another machine.";
                return RedirectToAction("Login", "Account");
            }

            try
            {
                LogActivity("SYNC_FEEDS", "Triggered live internet products synchronization via RSS feeds.");
                int syncedCount = await _discoveryService.SyncLiveFeedsAsync();
                TempData["SuccessMessage"] = $"✅ Discovered and synchronized {syncedCount} new live tech products successfully!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Failed to sync live feeds: {ex.Message}";
                LogActivity("SYNC_FEEDS_FAIL", $"RSS sync failed: {ex.Message}");
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult UpdateMobileNumber(string mobileNumber)
        {
            string loggedUser = HttpContext.Session.GetString("UserSession");
            if (loggedUser == null) return RedirectToAction("Login", "Account");
            if (!IsSessionValid())
            {
                HttpContext.Session.Clear();
                TempData["ErrorMessage"] = "⚠️ Session Terminated: Your account has been logged in on another machine.";
                return RedirectToAction("Login", "Account");
            }

            if (string.IsNullOrWhiteSpace(mobileNumber))
            {
                TempData["ErrorMessage"] = "❌ Mobile number cannot be empty.";
                return RedirectToAction(nameof(Index));
            }

            using (var connection = _context.CreateConnection())
            {
                string query = "UPDATE T_USERS SET F_MOBILE_NUMBER = @M WHERE F_USERNAME = @U";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    cmd.Parameters.AddWithValue("@M", mobileNumber.Trim());
                    cmd.Parameters.AddWithValue("@U", loggedUser);
                    connection.Open();
                    cmd.ExecuteNonQuery();
                }
            }

            LogActivity("PROFILE_UPDATE", $"User successfully updated their mobile number to: {mobileNumber.Trim()}");
            HttpContext.Session.Remove("PromptUpdateMobile"); // Remove the flag since it's set!
            TempData["SuccessMessage"] = "✅ Mobile number successfully updated!";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult AdministrativeAddUser(string username, string password, string mobile, string email)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();
            
            string uTrim = username.Trim();
            string pTrim = password.Trim();
            string mTrim = mobile.Trim();
            string eTrim = email?.Trim() ?? "";

            using (var conn = _context.CreateConnection()) {
                string query = "INSERT INTO T_USERS (F_USERNAME, F_PASSWORD, F_MOBILE_NUMBER, F_EMAIL, F_IS_APPROVED) VALUES (@U, @P, @M, @E, 1)";
                using (var cmd = new SqlCommand(query, (SqlConnection)conn)) {
                    cmd.Parameters.AddWithValue("@U", uTrim);
                    cmd.Parameters.AddWithValue("@P", pTrim);
                    cmd.Parameters.AddWithValue("@M", mTrim);
                    cmd.Parameters.AddWithValue("@E", string.IsNullOrEmpty(eTrim) ? DBNull.Value : (object)eTrim);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            
            // Dispatch credentials email immediately in background (prevents blocking admin console thread)
            if (!string.IsNullOrEmpty(eTrim))
            {
                string scheme = HttpContext.Request.Scheme;
                string host = HttpContext.Request.Host.ToUriComponent();
                string baseUrl = $"{scheme}://{host}";

                _ = Task.Run(() => {
                    try
                    {
                        string senderEmail = _configuration["EmailSettings:Username"]; 
                        string senderPassword = _configuration["EmailSettings:Password"]; 
                        using (MailMessage mail = new MailMessage()) { 
                            mail.From = new MailAddress(senderEmail, "ProductHub Admin"); 
                            mail.To.Add(eTrim); 
                            mail.Subject = "🎉 Welcome to ProductHub - Account Created by Administrator"; 
                            mail.IsBodyHtml = true;
                            mail.Body = $@"
                                <div style='font-family: &quot;Segoe UI&quot;, Tahoma, Geneva, Verdana, sans-serif; max-width: 600px; margin: auto; padding: 30px; border: 1px solid #e2e8f0; border-radius: 16px; background-color: #ffffff; box-shadow: 0 4px 12px rgba(0,0,0,0.02);'>
                                    <div style='text-align: center; margin-bottom: 24px;'>
                                        <div style='background-color: #E8F5E9; color: #16A34A; display: inline-block; padding: 12px; border-radius: 50%; margin-bottom: 12px;'>
                                            <svg width='32' height='32' fill='currentColor' viewBox='0 0 24 24' style='display: block;'>
                                                <path d='M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z'/>
                                            </svg>
                                        </div>
                                        <h2 style='color: #1E293B; margin: 0 0 8px 0; font-weight: 700; font-size: 22px;'>Welcome to ProductHub!</h2>
                                        <p style='color: #64748B; font-size: 14px; margin: 0;'>Your login profile has been successfully created by the Administrator.</p>
                                    </div>
                                    
                                    <div style='margin-bottom: 24px; line-height: 1.6; color: #334155; font-size: 15px;'>
                                        <p>Hello <strong>{uTrim}</strong>,</p>
                                        <p>Your account is ready! Below are your secure login credentials:</p>
                                        
                                        <div style='background-color: #F8FAFC; border: 1px solid #E2E8F0; border-radius: 8px; padding: 18px; margin: 20px 0;'>
                                            <table style='width: 100%; border-collapse: collapse; font-size: 14.5px;'>
                                                <tr>
                                                    <td style='padding: 6px 0; color: #64748B; width: 140px; font-weight: 500;'>Username:</td>
                                                    <td style='padding: 6px 0; color: #1E293B; font-weight: 700;'>{uTrim}</td>
                                                </tr>
                                                <tr>
                                                    <td style='padding: 6px 0; color: #64748B; font-weight: 500;'>Password:</td>
                                                    <td style='padding: 6px 0; color: #EF4444; font-weight: 700; font-family: monospace; font-size: 15.5px;'>{pTrim}</td>
                                                </tr>
                                                <tr>
                                                    <td style='padding: 6px 0; color: #64748B; font-weight: 500;'>Mobile Number:</td>
                                                    <td style='padding: 6px 0; color: #1E293B; font-weight: 600;'>{mTrim}</td>
                                                </tr>
                                            </table>
                                        </div>
                                        
                                        <div style='background-color: #FFFBEB; padding: 20px; border-left: 4px solid #D97706; margin: 24px 0; border-radius: 0 8px 8px 0;'>
                                            <p style='margin: 0; font-weight: 700; color: #B45309; font-size: 14.2px;'>⚠️ Action Required:</p>
                                            <p style='margin: 6px 0 0 0; color: #78350F; font-size: 13.5px;'>Please sign in and ensure that your profile information is up to date, including your <strong>mobile number</strong>, to secure your account and configure 2-Step OTP options.</p>
                                        </div>
                                    </div>
                                    
                                    <div style='text-align: center; margin-top: 30px;'>
                                        <a href='{baseUrl}' style='background-color: #16A34A; color: #ffffff; padding: 12px 28px; text-decoration: none; border-radius: 8px; font-weight: 600; display: inline-block; box-shadow: 0 2px 4px rgba(22,163,74,0.15); transition: background-color 0.15s ease;'>Log into ProductHub</a>
                                    </div>
                                    
                                    <hr style='border: 0; border-top: 1px solid #e2e8f0; margin: 30px 0;' />
                                    <div style='text-align: center; font-size: 12px; color: #94A3B8;'>
                                        This is a secure security notification from ProductHub.<br/>
                                        If you did not request this account activation, please contact system support.
                                    </div>
                                </div>"; 
                            
                            using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587)) { 
                                smtp.EnableSsl = true; 
                                smtp.UseDefaultCredentials = false; 
                                smtp.Credentials = new NetworkCredential(senderEmail, senderPassword); 
                                smtp.Send(mail); 
                            } 
                            SaveEmailBackup(mail.Subject, eTrim, mail.Body);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"SMTP Credentials Dispatch Error: {ex.Message}");
                    }
                });
            }

            LogActivity("USER_MANAGEMENT", $"Created brand-new login profile mapping entry row: '{uTrim}' linked with mobile: {mTrim} and email: {eTrim}");
            TempData["SuccessMessage"] = $"✅ User '{uTrim}' has been successfully created and their credentials have been dispatched to {eTrim}!";
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        public IActionResult AdministrativeDeleteUser(int userId, string targetName)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();
            using (var conn = _context.CreateConnection()) {
                string query = "DELETE FROM T_USERS WHERE F_USER_ID = @Id AND F_IS_ADMIN = 0";
                using (var cmd = new SqlCommand(query, (SqlConnection)conn)) {
                    cmd.Parameters.AddWithValue("@Id", userId);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            LogActivity("USER_MANAGEMENT", $"Pruned user account profile registry rows completely: '{targetName}'.");
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        public IActionResult AdministrativeChangeUserPassword(int userId, string targetName, string nextPassword)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();
            using (var conn = _context.CreateConnection()) {
                string query = "UPDATE T_USERS SET F_PASSWORD = @P WHERE F_USER_ID = @Id";
                using (var cmd = new SqlCommand(query, (SqlConnection)conn)) {
                    cmd.Parameters.AddWithValue("@P", nextPassword.Trim());
                    cmd.Parameters.AddWithValue("@Id", userId);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            LogActivity("PASSWORD_CHANGE", $"Forced password credential modification overwrite for account user: '{targetName}'.");
            return RedirectToAction(nameof(Users));
        }

        // =========================================================
        // 6. FILE STREAMS SYSTEM OPERATIONS PLUGINS (EPPLUS)
        // =========================================================
        public IActionResult DownloadTemplate()
        {
            LogActivity("DOWNLOAD", "Requested an empty standard Excel layout parsing template spreadsheet package file.");
            OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("ProductHub");
            using (var p = new ExcelPackage()) {
                var worksheet = p.Workbook.Worksheets.Add("Template");
                BuildExcelHeaderSchema(worksheet);
                return File(p.GetAsByteArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ProductTemplate.xlsx");
            }
        }

        public IActionResult ExportData(string brandFilter, double? minPrice, double? maxPrice, double? minRating)
        {
            LogActivity("EXPORT", "Generated spreadsheet download package compiling custom active catalog table filters data logs.");
            OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("ProductHub");
            List<Product> products = FetchFilteredProducts(brandFilter, minPrice, maxPrice, minRating, "");
            using (var package = new ExcelPackage()) {
                var worksheet = package.Workbook.Worksheets.Add("Records");
                BuildExcelHeaderSchema(worksheet);
                int r = 2;
                foreach (var p in products) {
                    worksheet.Cells[r, 1].Value = p.ProductName;
                    worksheet.Cells[r, 2].Value = p.Brand;
                    worksheet.Cells[r, 3].Value = p.Quantity;
                    worksheet.Cells[r, 4].Value = p.Price;
                    worksheet.Cells[r, 6].Value = p.ProductRating;
                    r++;
                }
                return File(package.GetAsByteArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ProductHub_Export.xlsx");
            }
        }

        [HttpPost]
        public IActionResult ImportData(IFormFile alexaExcelFile)
        {
            LogActivity("IMPORT", "Uploaded file spreadsheet stream dataset packages for core table processing matrix loops.");
            OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("ProductHub");
            using (var stream = new MemoryStream()) {
                alexaExcelFile.CopyTo(stream);
                using (var package = new ExcelPackage(stream)) {
                    var ws = package.Workbook.Worksheets.FirstOrDefault();
                    if (ws != null && ws.Dimension != null) {
                        using (var conn = _context.CreateConnection()) {
                            conn.Open();
                            for (int row = 2; row <= ws.Dimension.End.Row; row++) {
                                string name = ws.Cells[row, 1].Value?.ToString() ?? "";
                                if (string.IsNullOrEmpty(name)) continue;
                                string q = "INSERT INTO T_PRODUCTS (F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_RATING) VALUES (@N, @B, @Q, @P, @R)";
                                using (var cmd = new SqlCommand(q, (SqlConnection)conn)) {
                                    cmd.Parameters.AddWithValue("@N", name);
                                    cmd.Parameters.AddWithValue("@B", ws.Cells[row, 2].Value?.ToString() ?? "");
                                    cmd.Parameters.AddWithValue("@Q", ws.Cells[row, 3].Value?.ToString() ?? "");
                                    cmd.Parameters.AddWithValue("@P", Convert.ToDouble(ws.Cells[row, 4].Value ?? 0));
                                    cmd.Parameters.AddWithValue("@R", Convert.ToDouble(ws.Cells[row, 6].Value ?? 0));
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }
                    }
                }
            }
            return RedirectToAction(nameof(Index));
        }
        
        // =======================================================================================
        // 📧 UPDATED: SECURE ATTACHMENT PORTFOLIO PIPELINE WITH ADAPTIVE BANNERS & DYNAMIC STAMPS
        // =======================================================================================
        [HttpPost] 
        public async Task<IActionResult> EmailZipData(List<int> ids, string recipientEmail) 
        { 
            OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("ProductHub");

            // ✅ UPGRADE 1: Generate a unique dynamic time stamp name string for every bundle file
            string fileStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string dynamicZipFileName = $"ProductHub_Package_{fileStamp}.zip";

            LogActivity("EMAIL", $"Initialized compressed Zip spreadsheet archive broadcast routine for target: {recipientEmail}."); 
            
            string senderEmail = _configuration["EmailSettings:Username"]; 
            string senderPassword = _configuration["EmailSettings:Password"]; 
            byte[] excelBytes; 
            List<Product> prods = new List<Product>(); 

            try
            {
                using (var connection = _context.CreateConnection()) { 
                    var pNames = string.Join(",", ids.Select((id, idx) => $"@Id{idx}")); 
                    string query = $"SELECT F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_RATING FROM T_PRODUCTS WHERE F_PRODUCT_ID IN ({pNames})"; 
                    using (var cmd = new SqlCommand(query, (SqlConnection)connection)) { 
                        for (int i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue($"@Id{i}", ids[i]); 
                        connection.Open(); 
                        using (var reader = cmd.ExecuteReader()) { 
                            while (reader.Read()) prods.Add(new Product { ProductName = reader["F_PROD_NAME"].ToString() ?? "", Brand = reader["F_BRAND"].ToString() ?? "", Quantity = reader["F_QTY"].ToString() ?? "", Price = Convert.ToDouble(reader["F_PRICE"]), ProductRating = Convert.ToDouble(reader["F_PROD_RATING"]) }); 
                        } 
                    } 
                } 

                using (var package = new ExcelPackage()) { 
                    var ws = package.Workbook.Worksheets.Add("Report"); 
                    BuildExcelHeaderSchema(ws); 
                    int r = 2; 
                    foreach (var p in prods) { 
                        ws.Cells[r,1].Value = p.ProductName; 
                        ws.Cells[r,2].Value = p.Brand; 
                        ws.Cells[r,3].Value = p.Quantity; 
                        ws.Cells[r,4].Value = p.Price; 
                        ws.Cells[r,6].Value = p.ProductRating; 
                        r++; 
                    } 
                    excelBytes = package.GetAsByteArray(); 
                } 

                byte[] zipBytes; 
                using (var ms = new MemoryStream()) { 
                    using (var arc = new ZipArchive(ms, ZipArchiveMode.Create, true)) { 
                        var entry = arc.CreateEntry("Report.xlsx", System.IO.Compression.CompressionLevel.Optimal); 
                        using (var es = entry.Open()) es.Write(excelBytes, 0, excelBytes.Length); 
                    } 
                    zipBytes = ms.ToArray(); 
                } 

                using (MailMessage mail = new MailMessage()) { 
                    mail.From = new MailAddress(senderEmail); 
                    mail.To.Add(recipientEmail.Trim()); 
                    mail.Subject = "📦 Inventory Report Portfolio Package"; 
                    mail.Body = $"Attached Zip Sheet Archive.\nGenerated package tracking identifier: {dynamicZipFileName}"; 
                    
                    // Assign your dynamic name directly to the email attachment container
                    mail.Attachments.Add(new Attachment(new MemoryStream(zipBytes), dynamicZipFileName)); 
                    
                    using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587)) { 
                        smtp.EnableSsl = true; 
                        smtp.UseDefaultCredentials = false; 
                        smtp.Credentials = new NetworkCredential(senderEmail, senderPassword); 
                        await smtp.SendMailAsync(mail); 
                    } 
                }

                // ✅ UPGRADE 2: Return an elegant green success layout status context alert
                TempData["SuccessMessage"] = $"✉️ Email package containing '{dynamicZipFileName}' transmitted successfully over secure SMTP network tunnels to {recipientEmail}!";
            }
            catch (Exception ex)
            {
                // ✅ UPGRADE 3: Safely intercept transmission block exceptions (stops system crashing page errors)
                TempData["SuccessMessage"] = $"❌ Core Dispatch Failure: Unable to route SMTP packets to {recipientEmail}. Check network gateway or email structure configuration loops.";
                LogActivity("EMAIL_FAILURE", $"SMTP exception block captured during stream pipe: {ex.Message}");
            }

            return RedirectToAction(nameof(Index)); 
        }

        private void BuildExcelHeaderSchema(ExcelWorksheet sheet)
        {
            sheet.Cells[1, 1].Value = "Product Name";
            sheet.Cells[1, 2].Value = "Brand";
            sheet.Cells[1, 3].Value = "Quantity";
            sheet.Cells[1, 4].Value = "Price";
            sheet.Cells[1, 5].Value = "Description";
            sheet.Cells[1, 6].Value = "Rating";
        }

        private List<Product> FetchFilteredProducts(string brand, double? minP, double? maxP, double? minR, string sort, string category = null, string subcategory = null, bool approvedOnly = false)
        {
            List<Product> list = new List<Product>();
            using (var connection = _context.CreateConnection()) {
                string query = @"
                    SELECT p.F_PRODUCT_ID, p.F_PROD_NAME, COALESCE(b.BrandName, p.F_BRAND) AS F_BRAND, p.F_QTY, p.F_PRICE, p.F_PROD_DESC, p.F_PROD_RATING, COALESCE(c.CategoryName, p.F_CATEGORY) AS F_CATEGORY, p.F_SUBCATEGORY, p.F_LAUNCH_DATE, p.F_WEBSITE, p.F_AI_SUMMARY, p.F_WIKIPEDIA_URL, p.F_IMAGE_URL, p.F_ARTICLE_URL, p.F_IS_APPROVED, p.F_CATEGORY_ID, p.F_BRAND_ID 
                    FROM T_PRODUCTS p
                    LEFT JOIN T_BRANDS b ON p.F_BRAND_ID = b.BrandId
                    LEFT JOIN T_CATEGORIES c ON p.F_CATEGORY_ID = c.CategoryId
                    WHERE 1=1";

                if (!string.IsNullOrEmpty(brand) && brand != "ALL") {
                    if (brand.Contains(",")) {
                        var brandList = brand.Split(',').Select(b => b.Trim()).ToList();
                        var clauses = new List<string>();
                        for (int i = 0; i < brandList.Count; i++) {
                            clauses.Add($"(b.BrandName LIKE @B{i} OR p.F_BRAND LIKE @B{i})");
                        }
                        query += " AND (" + string.Join(" OR ", clauses) + ")";
                    } else {
                        query += " AND (b.BrandName LIKE @B OR p.F_BRAND LIKE @B)";
                    }
                }
                if (minP.HasValue) query += " AND p.F_PRICE >= @MinP";
                if (maxP.HasValue) query += " AND p.F_PRICE <= @MaxP";
                if (minR.HasValue) query += " AND p.F_PROD_RATING >= @MinR";
                if (!string.IsNullOrEmpty(category)) query += " AND (c.CategoryName = @Category OR p.F_CATEGORY = @Category)";
                if (!string.IsNullOrEmpty(subcategory)) query += " AND p.F_SUBCATEGORY = @Subcategory";
                if (approvedOnly) query += " AND p.F_IS_APPROVED = 1";

                if (sort == "Trending") query += " ORDER BY p.F_PROD_RATING DESC";
                else if (sort == "Latest") query += " ORDER BY p.F_LAUNCH_DATE DESC";

                using (var cmd = new SqlCommand(query, (SqlConnection)connection)) {
                    if (!string.IsNullOrEmpty(brand) && brand != "ALL") {
                        if (brand.Contains(",")) {
                            var brandList = brand.Split(',').Select(b => b.Trim()).ToList();
                            for (int i = 0; i < brandList.Count; i++) {
                                cmd.Parameters.AddWithValue($"@B{i}", "%" + brandList[i] + "%");
                            }
                        } else {
                            cmd.Parameters.AddWithValue("@B", "%" + brand + "%");
                        }
                    }
                    cmd.Parameters.AddWithValue("@MinP", minP ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@MaxP", maxP ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@MinR", minR ?? (object)DBNull.Value);
                    if (!string.IsNullOrEmpty(category)) cmd.Parameters.AddWithValue("@Category", category);
                    if (!string.IsNullOrEmpty(subcategory)) cmd.Parameters.AddWithValue("@Subcategory", subcategory);

                    connection.Open();
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read())
                            list.Add(new Product {
                                ProductId = Convert.ToInt32(reader["F_PRODUCT_ID"]),
                                ProductName = reader["F_PROD_NAME"].ToString() ?? "",
                                Brand = reader["F_BRAND"].ToString() ?? "",
                                Quantity = reader["F_QTY"].ToString() ?? "",
                                Price = Convert.ToDouble(reader["F_PRICE"] == DBNull.Value ? 0 : reader["F_PRICE"]),
                                ProductDescription = reader["F_PROD_DESC"]?.ToString(),
                                ProductRating = Convert.ToDouble(reader["F_PROD_RATING"] == DBNull.Value ? 0 : reader["F_PROD_RATING"]),
                                Category = reader["F_CATEGORY"]?.ToString(),
                                Subcategory = reader["F_SUBCATEGORY"]?.ToString(),
                                LaunchDate = reader["F_LAUNCH_DATE"] != DBNull.Value ? Convert.ToDateTime(reader["F_LAUNCH_DATE"]) : (DateTime?)null,
                                Website = reader["F_WEBSITE"]?.ToString(),
                                AiSummary = reader["F_AI_SUMMARY"]?.ToString(),
                                WikipediaUrl = reader["F_WIKIPEDIA_URL"]?.ToString(),
                                ImageUrl = reader["F_IMAGE_URL"]?.ToString(),
                                ArticleUrl = reader["F_ARTICLE_URL"]?.ToString(),
                                IsApproved = reader["F_IS_APPROVED"] == DBNull.Value ? true : Convert.ToBoolean(reader["F_IS_APPROVED"]),
                                CategoryId = reader["F_CATEGORY_ID"] != DBNull.Value ? Convert.ToInt32(reader["F_CATEGORY_ID"]) : (int?)null,
                                BrandId = reader["F_BRAND_ID"] != DBNull.Value ? Convert.ToInt32(reader["F_BRAND_ID"]) : (int?)null
                            });
                    }
                }
            }
            return list;
        }

        [HttpPost]
        public async Task<IActionResult> RefreshProductInfo(int id)
        {
            var p = GetProductById(id);
            if (p != null) {
                await TriggerOnDemandEnrichmentAsync(p);
                TempData["SuccessMessage"] = "🔄 Product specs and description enriched successfully from live internet sources!";
            }
            return RedirectToAction("Details", new { id });
        }

        [HttpPost]
        public async Task<IActionResult> RefreshInternetImages(int id)
        {
            var p = GetProductById(id);
            if (p != null) {
                var enrichment = await _discoveryService.FetchEnrichmentAsync(p.ProductName, p.Brand);
                if (enrichment != null && enrichment.Images.Count > 0) {
                    using (var conn = _context.CreateConnection()) {
                        conn.Open();
                        string clearQ = "DELETE FROM T_PRODUCT_IMAGES WHERE ProductId=@Id";
                        using (var cmd = new SqlCommand(clearQ, (SqlConnection)conn)) {
                            cmd.Parameters.AddWithValue("@Id", id);
                            cmd.ExecuteNonQuery();
                        }
                        string imgQuery = "INSERT INTO T_PRODUCT_IMAGES (ProductId, ImageUrl, Source, IsPrimary) VALUES (@ProductId, @ImageUrl, @Source, @IsPrimary)";
                        bool first = true;
                        foreach (var img in enrichment.Images) {
                            using (var imgCmd = new SqlCommand(imgQuery, (SqlConnection)conn)) {
                                imgCmd.Parameters.AddWithValue("@ProductId", id);
                                imgCmd.Parameters.AddWithValue("@ImageUrl", img);
                                imgCmd.Parameters.AddWithValue("@Source", "AI Scraper");
                                imgCmd.Parameters.AddWithValue("@IsPrimary", first);
                                imgCmd.ExecuteNonQuery();
                            }
                            first = false;
                        }
                    }
                    TempData["SuccessMessage"] = "🖼️ Product image gallery refreshed successfully from online image sources!";
                }
            }
            return RedirectToAction("Details", new { id });
        }

        [HttpPost]
        public IActionResult ApproveDiscoveredProduct(int id)
        {
            using (var conn = _context.CreateConnection()) {
                string q = "UPDATE T_PRODUCTS SET F_IS_APPROVED=1 WHERE F_PRODUCT_ID=@Id";
                using (var cmd = new SqlCommand(q, (SqlConnection)conn)) {
                    cmd.Parameters.AddWithValue("@Id", id);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            LogActivity("APPROVE_PRODUCT", $"Approved discovered product item ID: {id}");
            TempData["SuccessMessage"] = "✅ Discovered product approved and published to the active store catalog!";
            return RedirectToAction("Details", new { id });
        }

        [HttpPost]
        public IActionResult ReclassifyProduct(int id, string category, string subcategory)
        {
            using (var conn = _context.CreateConnection()) {
                string q = "UPDATE T_PRODUCTS SET F_CATEGORY=@Category, F_SUBCATEGORY=@Subcategory WHERE F_PRODUCT_ID=@Id";
                using (var cmd = new SqlCommand(q, (SqlConnection)conn)) {
                    cmd.Parameters.AddWithValue("@Category", category);
                    cmd.Parameters.AddWithValue("@Subcategory", subcategory);
                    cmd.Parameters.AddWithValue("@Id", id);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            LogActivity("RECLASSIFY", $"Reclassified product item ID {id} to: {category} -> {subcategory}");
            TempData["SuccessMessage"] = $"🏷️ Product reclassified to {category} > {subcategory}!";
            return RedirectToAction("Details", new { id });
        }

        [HttpPost]
        public IActionResult SetPrimaryImage(int productId, int imageId)
        {
            string imageUrl = "";
            using (var conn = _context.CreateConnection()) {
                conn.Open();
                string getQ = "SELECT ImageUrl FROM T_PRODUCT_IMAGES WHERE ImageId=@ImageId";
                using (var cmd = new SqlCommand(getQ, (SqlConnection)conn)) {
                    cmd.Parameters.AddWithValue("@ImageId", imageId);
                    imageUrl = cmd.ExecuteScalar()?.ToString() ?? "";
                }
                if (!string.IsNullOrEmpty(imageUrl)) {
                    string updateQ = "UPDATE T_PRODUCTS SET F_IMAGE_URL=@Url WHERE F_PRODUCT_ID=@ProductId";
                    using (var cmd = new SqlCommand(updateQ, (SqlConnection)conn)) {
                        cmd.Parameters.AddWithValue("@Url", imageUrl);
                        cmd.Parameters.AddWithValue("@ProductId", productId);
                        cmd.ExecuteNonQuery();
                    }
                    string resetQ = "UPDATE T_PRODUCT_IMAGES SET IsPrimary=0 WHERE ProductId=@ProductId; UPDATE T_PRODUCT_IMAGES SET IsPrimary=1 WHERE ImageId=@ImageId;";
                    using (var cmd = new SqlCommand(resetQ, (SqlConnection)conn)) {
                        cmd.Parameters.AddWithValue("@ProductId", productId);
                        cmd.Parameters.AddWithValue("@ImageId", imageId);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            TempData["SuccessMessage"] = "⭐ Primary display image updated successfully!";
            return RedirectToAction("Details", new { id = productId });
        }

        [HttpGet]
        public async Task<IActionResult> Search(string q)
        {
            if (HttpContext.Session.GetString("UserSession") == null) return RedirectToAction("Login", "Account");
            if (string.IsNullOrEmpty(q)) return RedirectToAction("Index");

            string cacheKey = $"search_{q.Trim().ToLower()}";
            if (_memoryCache.TryGetValue(cacheKey, out AiSearchResponse cachedResponse))
            {
                ViewBag.Query = q;
                return View(cachedResponse);
            }

            var response = new AiSearchResponse { Answer = "" };

            // Log Search query
            try {
                using (var conn = _context.CreateConnection()) {
                    string analyticsQ = "INSERT INTO T_PRODUCT_ANALYTICS (ProductId, SearchQuery, UserSession) VALUES (NULL, @Q, @User)";
                    using (var cmd = new SqlCommand(analyticsQ, (SqlConnection)conn)) {
                        cmd.Parameters.AddWithValue("@Q", q);
                        cmd.Parameters.AddWithValue("@User", HttpContext.Session.GetString("UserSession") ?? "Anonymous");
                        conn.Open();
                        cmd.ExecuteNonQuery();
                    }
                }
            } catch { }

            // 1. Search database first
            var dbProducts = new List<Product>();
            try
            {
                using (var conn = _context.CreateConnection())
                {
                    string selectQ = @"
                        SELECT TOP 10 p.F_PRODUCT_ID, p.F_PROD_NAME, COALESCE(b.BrandName, p.F_BRAND) AS F_BRAND, p.F_QTY, p.F_PRICE, p.F_PROD_DESC, p.F_PROD_RATING, COALESCE(c.CategoryName, p.F_CATEGORY) AS F_CATEGORY, p.F_IMAGE_URL 
                        FROM T_PRODUCTS p
                        LEFT JOIN T_BRANDS b ON p.F_BRAND_ID = b.BrandId
                        LEFT JOIN T_CATEGORIES c ON p.F_CATEGORY_ID = c.CategoryId
                        WHERE p.F_PROD_NAME LIKE @Q OR p.F_BRAND LIKE @Q OR b.BrandName LIKE @Q";
                    using (var cmd = new SqlCommand(selectQ, (SqlConnection)conn))
                    {
                        cmd.Parameters.AddWithValue("@Q", "%" + q + "%");
                        conn.Open();
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                dbProducts.Add(new Product
                                {
                                    ProductId = Convert.ToInt32(reader["F_PRODUCT_ID"]),
                                    ProductName = reader["F_PROD_NAME"].ToString() ?? "",
                                    Brand = reader["F_BRAND"].ToString() ?? "",
                                    Price = Convert.ToDouble(reader["F_PRICE"] == DBNull.Value ? 0 : reader["F_PRICE"]),
                                    ProductDescription = reader["F_PROD_DESC"]?.ToString(),
                                    ImageUrl = reader["F_IMAGE_URL"]?.ToString(),
                                    Category = reader["F_CATEGORY"]?.ToString()
                                });
                            }
                        }
                    }
                }
            }
            catch { }

            if (dbProducts.Count > 0)
            {
                // Found in Database: Call Python generate-summary (No web scraping)
                var localCards = new List<ProductCardDto>();
                var localImages = new List<string>();
                foreach (var p in dbProducts)
                {
                    localCards.Add(new ProductCardDto
                    {
                        Id = p.ProductId.ToString(),
                        Name = p.ProductName,
                        Brand = p.Brand,
                        Category = p.Category ?? "Electronics",
                        Description = p.ProductDescription ?? "Local inventory item."
                    });
                    if (!string.IsNullOrEmpty(p.ImageUrl))
                    {
                        localImages.Add(p.ImageUrl);
                    }
                }

                string summary = "";
                try
                {
                    using (var client = new HttpClient())
                    {
                        var payload = new
                        {
                            query = q,
                            products = dbProducts.Select(p => new {
                                name = p.ProductName,
                                brand = p.Brand,
                                price = p.Price,
                                description = p.ProductDescription ?? ""
                            }).ToList()
                        };
                        var apiRes = await client.PostAsJsonAsync("http://localhost:8000/generate-summary", payload);
                        if (apiRes.IsSuccessStatusCode)
                        {
                            var resData = await apiRes.Content.ReadFromJsonAsync<JsonElement>();
                            if (resData.TryGetProperty("answer", out var ansVal))
                            {
                                summary = ansVal.GetString() ?? "";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    summary = $"### AI Summary Generation Offline\n\nGenerated from local database: {dbProducts.Count} products matched. LLM server offline: {ex.Message}";
                }

                if (string.IsNullOrEmpty(summary))
                {
                    summary = $"### Product intelligence report for **{q}**\n\nFound {dbProducts.Count} matching item(s) in local catalog:\n\n" +
                              string.Join("\n", dbProducts.Select(p => $"* **{p.ProductName}** ({p.Brand}) - Rs. {p.Price:N0}"));
                }

                response.Answer = summary;
                response.ProductCards = localCards;
                response.Images = localImages.Take(6).ToList();
                response.Sources = new List<SourceDto> { new SourceDto { Id = 1, Name = "Local Database", Url = "/Product" } };
                response.RelatedProducts = new List<string> { $"Detailed review of {dbProducts[0].ProductName}", $"Compare {q} with alternatives" };

                _memoryCache.Set(cacheKey, response, TimeSpan.FromMinutes(10));
            }
            else
            {
                // Not found in Database: Call Python AI-Search ( DuckDuckGo scraping fallback)
                try {
                    using (var client = new HttpClient()) {
                        var apiResponse = await client.PostAsJsonAsync("http://localhost:8000/ai-search", new { query = q });
                        if (apiResponse.IsSuccessStatusCode) {
                            response = await apiResponse.Content.ReadFromJsonAsync<AiSearchResponse>() ?? response;

                            // Cache newly discovered products to SQL database
                            foreach (var card in response.ProductCards)
                            {
                                if (!ProductExistsByName(card.Name))
                                {
                                    int newPid = InsertScrapedProduct(card, response.Images);
                                    card.Id = newPid.ToString();
                                }
                            }

                            _memoryCache.Set(cacheKey, response, TimeSpan.FromMinutes(10));
                        }
                    }
                } catch (Exception ex) {
                    response.Answer = $"### Error querying AI Search\n\nCould not reach the local AI service. Details: {ex.Message}. Make sure Python FastAPI is running on port 8000.";
                }
            }

            ViewBag.Query = q;
            return View(response);
        }

        private bool ProductExistsByName(string name)
        {
            try {
                using (var conn = _context.CreateConnection())
                {
                    string query = "SELECT COUNT(1) FROM T_PRODUCTS WHERE F_PROD_NAME = @Name";
                    using (var cmd = new SqlCommand(query, (SqlConnection)conn))
                    {
                        cmd.Parameters.AddWithValue("@Name", name);
                        conn.Open();
                        return (int)cmd.ExecuteScalar() > 0;
                    }
                }
            } catch { return false; }
        }

        private int InsertScrapedProduct(ProductCardDto card, List<string> images)
        {
            int newId = 0;
            try {
                GetOrCreateBrandAndCategoryIds(card.Brand, card.Category, out int? brandId, out int? categoryId);
                
                using (var conn = _context.CreateConnection())
                {
                    conn.Open();

                    // 1. Insert product
                    string query = @"
                        INSERT INTO T_PRODUCTS 
                        (F_PROD_NAME, F_BRAND, F_BRAND_ID, F_QTY, F_PRICE, F_PROD_DESC, F_PROD_RATING, F_CATEGORY, F_CATEGORY_ID, F_WEBSITE, F_IS_APPROVED)
                        VALUES 
                        (@Name, @Brand, @BrandId, 'Discovered', 0.0, @Desc, 4.0, @Category, @CategoryId, @Url, 1);
                        SELECT SCOPE_IDENTITY();";

                    using (var cmd = new SqlCommand(query, (SqlConnection)conn))
                    {
                        cmd.Parameters.AddWithValue("@Name", card.Name);
                        cmd.Parameters.AddWithValue("@Brand", card.Brand ?? "Web");
                        cmd.Parameters.AddWithValue("@BrandId", brandId ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Desc", card.Description ?? "");
                        cmd.Parameters.AddWithValue("@Category", card.Category ?? "Electronics");
                        cmd.Parameters.AddWithValue("@CategoryId", categoryId ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Url", card.Url ?? "");

                        newId = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    // 2. Insert image as primary image if available
                    if (images != null && images.Count > 0)
                    {
                        string imgQ = "INSERT INTO T_PRODUCT_IMAGES (ProductId, ImageUrl, Source, IsPrimary) VALUES (@ProductId, @ImageUrl, 'AI Scraper', 1)";
                        using (var cmd = new SqlCommand(imgQ, (SqlConnection)conn))
                        {
                            cmd.Parameters.AddWithValue("@ProductId", newId);
                            cmd.Parameters.AddWithValue("@ImageUrl", images[0]);
                            cmd.ExecuteNonQuery();
                        }

                        // Update F_IMAGE_URL on T_PRODUCTS
                        string updQ = "UPDATE T_PRODUCTS SET F_IMAGE_URL = @Url WHERE F_PRODUCT_ID = @Id";
                        using (var cmd = new SqlCommand(updQ, (SqlConnection)conn))
                        {
                            cmd.Parameters.AddWithValue("@Url", images[0]);
                            cmd.Parameters.AddWithValue("@Id", newId);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    // 3. Save source URL
                    if (!string.IsNullOrEmpty(card.Url))
                    {
                        string srcQ = "INSERT INTO T_PRODUCT_SOURCES (ProductId, SourceUrl, SourceName) VALUES (@ProductId, @Url, 'Discovered Link')";
                        using (var cmd = new SqlCommand(srcQ, (SqlConnection)conn))
                        {
                            cmd.Parameters.AddWithValue("@ProductId", newId);
                            cmd.Parameters.AddWithValue("@Url", card.Url);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            } catch { }
            return newId;
        }

        [HttpGet]
        public async Task<IActionResult> Category(string cat, string sub)
        {
            if (HttpContext.Session.GetString("UserSession") == null) return RedirectToAction("Login", "Account");
            
            ViewBag.LoggedUser = HttpContext.Session.GetString("UserSession");
            ViewBag.IsAdmin = HttpContext.Session.GetInt32("IsAdmin") ?? 0;
            
            List<Product> products = FetchFilteredProducts(
                brand: null, minP: null, maxP: null, minR: null, sort: "Latest",
                category: cat, subcategory: sub
            );

            string aiInsights = "<div class='text-center p-4'><i class='fa-solid fa-circle-notch fa-spin fa-2x text-indigo-400 mb-3'></i><br/>Loading AI market insights dynamically...</div>";

            ViewBag.CategoryName = cat;
            ViewBag.SubcategoryName = sub;
            ViewBag.AiInsights = aiInsights;

            return View(products);
        }

        [HttpGet]
        public IActionResult Dashboard()
        {
            if (HttpContext.Session.GetString("UserSession") == null) return RedirectToAction("Login", "Account");

            var trending = new List<Product>();
            var latest = new List<Product>();
            var mostViewed = new List<Product>();
            var newThisWeek = new List<Product>();
            var topCategories = new List<CategoryStat>();
            var searchQueries = new List<SearchQueryStat>();

            using (var conn = _context.CreateConnection()) {
                conn.Open();

                string trendQ = "SELECT TOP 5 F_PRODUCT_ID, F_PROD_NAME, F_BRAND, F_PRICE, F_PROD_RATING, F_IMAGE_URL FROM T_PRODUCTS ORDER BY F_PROD_RATING DESC";
                using (var cmd = new SqlCommand(trendQ, (SqlConnection)conn)) {
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) trending.Add(new Product { ProductId = Convert.ToInt32(r["F_PRODUCT_ID"]), ProductName = r["F_PROD_NAME"].ToString() ?? "", Brand = r["F_BRAND"].ToString() ?? "", Price = Convert.ToDouble(r["F_PRICE"]), ProductRating = Convert.ToDouble(r["F_PROD_RATING"]), ImageUrl = r["F_IMAGE_URL"]?.ToString() });
                }

                string lateQ = "SELECT TOP 5 F_PRODUCT_ID, F_PROD_NAME, F_BRAND, F_PRICE, F_PROD_RATING, F_IMAGE_URL FROM T_PRODUCTS WHERE F_LAUNCH_DATE IS NOT NULL ORDER BY F_LAUNCH_DATE DESC";
                using (var cmd = new SqlCommand(lateQ, (SqlConnection)conn)) {
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) latest.Add(new Product { ProductId = Convert.ToInt32(r["F_PRODUCT_ID"]), ProductName = r["F_PROD_NAME"].ToString() ?? "", Brand = r["F_BRAND"].ToString() ?? "", Price = Convert.ToDouble(r["F_PRICE"]), ProductRating = Convert.ToDouble(r["F_PROD_RATING"]), ImageUrl = r["F_IMAGE_URL"]?.ToString() });
                }

                string viewQ = "SELECT TOP 5 p.F_PRODUCT_ID, p.F_PROD_NAME, p.F_BRAND, p.F_PRICE, a.F_VIEW_COUNT FROM T_PRODUCTS p JOIN T_ANALYTICS a ON p.F_PRODUCT_ID = a.F_PRODUCT_ID ORDER BY a.F_VIEW_COUNT DESC";
                using (var cmd = new SqlCommand(viewQ, (SqlConnection)conn)) {
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) mostViewed.Add(new Product { ProductId = Convert.ToInt32(r["F_PRODUCT_ID"]), ProductName = r["F_PROD_NAME"].ToString() ?? "", Brand = r["F_BRAND"].ToString() ?? "", Price = Convert.ToDouble(r["F_PRICE"]), Quantity = r["F_VIEW_COUNT"].ToString() }); 
                }

                string weekQ = "SELECT F_PRODUCT_ID, F_PROD_NAME, F_BRAND, F_PRICE, F_PROD_RATING FROM T_PRODUCTS WHERE F_LAUNCH_DATE >= DATEADD(day, -7, GETDATE())";
                using (var cmd = new SqlCommand(weekQ, (SqlConnection)conn)) {
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) newThisWeek.Add(new Product { ProductId = Convert.ToInt32(r["F_PRODUCT_ID"]), ProductName = r["F_PROD_NAME"].ToString() ?? "", Brand = r["F_BRAND"].ToString() ?? "", Price = Convert.ToDouble(r["F_PRICE"]), ProductRating = Convert.ToDouble(r["F_PROD_RATING"]) });
                }

                string catQ = "SELECT F_CATEGORY, COUNT(1) AS Cnt FROM T_PRODUCTS WHERE F_CATEGORY IS NOT NULL GROUP BY F_CATEGORY ORDER BY Cnt DESC";
                using (var cmd = new SqlCommand(catQ, (SqlConnection)conn)) {
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) topCategories.Add(new CategoryStat { Category = r["F_CATEGORY"].ToString() ?? "", Count = Convert.ToInt32(r["Cnt"]) });
                }

                string searchQ = "SELECT TOP 10 SearchQuery, COUNT(1) AS Cnt FROM T_PRODUCT_ANALYTICS WHERE SearchQuery IS NOT NULL GROUP BY SearchQuery ORDER BY Cnt DESC";
                using (var cmd = new SqlCommand(searchQ, (SqlConnection)conn)) {
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) searchQueries.Add(new SearchQueryStat { Query = r["SearchQuery"].ToString() ?? "", Count = Convert.ToInt32(r["Cnt"]) });
                }
            }

            ViewBag.Trending = trending;
            ViewBag.Latest = latest;
            ViewBag.MostViewed = mostViewed;
            ViewBag.NewThisWeek = newThisWeek;
            ViewBag.TopCategories = topCategories;
            ViewBag.SearchQueries = searchQueries;

            return View();
        }

        private Product GetProductById(int id)
        {
            Product product = null;
            using (var connection = _context.CreateConnection()) {
                string query = "SELECT F_PRODUCT_ID, F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_DESC, F_PROD_RATING, F_CATEGORY, F_SUBCATEGORY, F_LAUNCH_DATE, F_WEBSITE, F_AI_SUMMARY, F_WIKIPEDIA_URL, F_IMAGE_URL, F_ARTICLE_URL FROM T_PRODUCTS WHERE F_PRODUCT_ID = @Id";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection)) {
                    cmd.Parameters.AddWithValue("@Id", id);
                    connection.Open();
                    using (var reader = cmd.ExecuteReader()) {
                        if (reader.Read()) {
                            product = new Product {
                                ProductId = Convert.ToInt32(reader["F_PRODUCT_ID"]),
                                ProductName = reader["F_PROD_NAME"].ToString() ?? "",
                                Brand = reader["F_BRAND"].ToString() ?? "",
                                Quantity = reader["F_QTY"].ToString() ?? "",
                                Price = Convert.ToDouble(reader["F_PRICE"] == DBNull.Value ? 0 : reader["F_PRICE"]),
                                ProductDescription = reader["F_PROD_DESC"]?.ToString(),
                                ProductRating = Convert.ToDouble(reader["F_PROD_RATING"] == DBNull.Value ? 0 : reader["F_PROD_RATING"]),
                                Category = reader["F_CATEGORY"]?.ToString(),
                                Subcategory = reader["F_SUBCATEGORY"]?.ToString(),
                                LaunchDate = reader["F_LAUNCH_DATE"] != DBNull.Value ? Convert.ToDateTime(reader["F_LAUNCH_DATE"]) : (DateTime?)null,
                                Website = reader["F_WEBSITE"]?.ToString(),
                                AiSummary = reader["F_AI_SUMMARY"]?.ToString(),
                                WikipediaUrl = reader["F_WIKIPEDIA_URL"]?.ToString(),
                                ImageUrl = reader["F_IMAGE_URL"]?.ToString(),
                                ArticleUrl = reader["F_ARTICLE_URL"]?.ToString()
                            };
                        }
                    }
                }
            }
            return product;
        }

        public class CategoryStat {
            public string Category { get; set; } = "";
            public int Count { get; set; }
        }

        public class SearchQueryStat {
            public string Query { get; set; } = "";
            public int Count { get; set; }
        }
    }
}
