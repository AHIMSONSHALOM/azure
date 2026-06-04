using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductHub_MVC.Models
{
    [Table("T_CATEGORIES")]
    public class Category
    {
        [Key]
        [Column("CategoryId")]
        public int CategoryId { get; set; }

        [Required]
        [Column("CategoryName")]
        public string CategoryName { get; set; } = string.Empty;
    }
}
