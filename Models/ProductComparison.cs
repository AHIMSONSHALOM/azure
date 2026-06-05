using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductHub_MVC.Models
{
    [Table("T_PRODUCT_COMPARISONS")]
    public class ProductComparison
    {
        [Key]
        [Column("F_COMPARISON_ID")]
        public int ComparisonId { get; set; }

        [Required]
        [Column("F_USER_SESSION")]
        public string UserSession { get; set; } = string.Empty;

        [Required]
        [Column("F_PRODUCT_IDS")]
        public string ProductIds { get; set; } = string.Empty;

        [Column("F_CREATED_AT")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
