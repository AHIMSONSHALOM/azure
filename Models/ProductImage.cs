using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductHub_MVC.Models
{
    [Table("T_PRODUCT_IMAGES")]
    public class ProductImage
    {
        [Key]
        [Column("ImageId")]
        public int ImageId { get; set; }

        public int ProductId { get; set; }

        public string ImageUrl { get; set; } = string.Empty;

        public string? Source { get; set; }

        public string? SourceUrl { get; set; }

        public bool IsPrimary { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
