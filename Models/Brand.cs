using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductHub_MVC.Models
{
    [Table("T_BRANDS")]
    public class Brand
    {
        [Key]
        [Column("BrandId")]
        public int BrandId { get; set; }

        [Required]
        [Column("BrandName")]
        public string BrandName { get; set; } = string.Empty;

        [Column("WebsiteUrl")]
        public string? WebsiteUrl { get; set; }
    }
}
