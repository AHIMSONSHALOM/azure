using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductHub_MVC.Models
{
    [Table("T_PRODUCT_SOURCES")]
    public class ProductSource
    {
        [Key]
        [Column("SourceId")]
        public int SourceId { get; set; }

        public int ProductId { get; set; }

        public string SourceUrl { get; set; } = string.Empty;

        public string SourceName { get; set; } = string.Empty;
    }
}
