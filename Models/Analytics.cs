using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductHub_MVC.Models
{
    [Table("T_ANALYTICS")]
    public class Analytics
    {
        [Key]
        [Column("F_ANALYTICS_ID")]
        public int AnalyticsId { get; set; }

        [Column("F_PRODUCT_ID")]
        public int ProductId { get; set; }

        [Column("F_VIEW_COUNT")]
        public int ViewCount { get; set; }

        [Column("F_LAST_VIEWED")]
        public DateTime? LastViewed { get; set; }
    }
}
