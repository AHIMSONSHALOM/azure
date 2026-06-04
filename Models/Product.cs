using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductHub_MVC.Models
{
    [Table("T_PRODUCTS")]
    public class Product
    {
        [Key]
        [Column("F_PRODUCT_ID")]
        public int ProductId { get; set; } 

        [Required(ErrorMessage = "Product name is required.")]
        [Column("F_PROD_NAME")]
        public string ProductName { get; set; } = string.Empty; 

        [Required(ErrorMessage = "Brand name is required.")]
        [Column("F_BRAND")]
        public string Brand { get; set; } = string.Empty; 

        [Required(ErrorMessage = "Quantity detail is required.")]
        [Column("F_QTY")]
        public string Quantity { get; set; } = string.Empty; 

        [Required(ErrorMessage = "Price is required.")]
        [Column("F_PRICE")]
        public double Price { get; set; } 

        [Column("F_PROD_DESC")]
        public string? ProductDescription { get; set; } 

        [Column("F_PROD_RATING")]
        public double ProductRating { get; set; } 

        [Column("F_CATEGORY")]
        public string? Category { get; set; }

        [Column("F_CATEGORY_ID")]
        public int? CategoryId { get; set; }

        [Column("F_BRAND_ID")]
        public int? BrandId { get; set; }

        [Column("F_LAUNCH_DATE")]
        public DateTime? LaunchDate { get; set; }

        [Column("F_WEBSITE")]
        public string? Website { get; set; }

        [Column("F_AI_SUMMARY")]
        public string? AiSummary { get; set; }

        [Column("F_WIKIPEDIA_URL")]
        public string? WikipediaUrl { get; set; }

        [Column("F_IMAGE_URL")]
        public string? ImageUrl { get; set; }

        [Column("F_ARTICLE_URL")]
        public string? ArticleUrl { get; set; }

        [Column("F_SUBCATEGORY")]
        public string? Subcategory { get; set; }

        [NotMapped]
        public List<ProductImage> ProductImages { get; set; } = new();

        [NotMapped]
        public List<ProductSource> ProductSources { get; set; } = new();

        [NotMapped]
        public List<ProductNews> ProductNews { get; set; } = new();

        [Column("F_IS_APPROVED")]
        public bool IsApproved { get; set; } = true;
    }
}