using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductHub_MVC.Models
{
    [Table("T_PRODUCT_ANALYTICS")]
    public class ProductAnalytics
    {
        [Key]
        [Column("AnalyticsId")]
        public int AnalyticsId { get; set; }

        public int? ProductId { get; set; }

        public string? SearchQuery { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string? UserSession { get; set; }

        public int Views { get; set; }

        public int Searches { get; set; }

        public DateTime? LastViewed { get; set; }
    }
}
