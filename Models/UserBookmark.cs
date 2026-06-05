using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductHub_MVC.Models
{
    [Table("T_USER_BOOKMARKS")]
    public class UserBookmark
    {
        [Key]
        [Column("F_BOOKMARK_ID")]
        public int BookmarkId { get; set; }

        [Required]
        [Column("F_USER_SESSION")]
        public string UserSession { get; set; } = string.Empty;

        [Column("F_PRODUCT_ID")]
        public int ProductId { get; set; }

        [Column("F_CREATED_AT")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey("ProductId")]
        public virtual Product? Product { get; set; }
    }
}
