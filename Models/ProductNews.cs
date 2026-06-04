using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductHub_MVC.Models
{
    [Table("T_PRODUCT_NEWS")]
    public class ProductNews
    {
        [Key]
        [Column("NewsId")]
        public int NewsId { get; set; }

        public int ProductId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        public string? Summary { get; set; }

        public DateTime? PublishDate { get; set; }

        public DateTime? PublishedDate { get; set; }
    }
}
