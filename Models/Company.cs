using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductHub_MVC.Models
{
    [Table("T_COMPANIES")]
    public class Company
    {
        [Key]
        [Column("F_COMPANY_ID")]
        public int CompanyId { get; set; }

        [Required]
        [Column("F_COMPANY_NAME")]
        public string CompanyName { get; set; } = string.Empty;

        [Column("F_WIKIDATA_ID")]
        public string? WikidataId { get; set; }

        [Column("F_DESCRIPTION")]
        public string? Description { get; set; }

        [Column("F_WEBSITE")]
        public string? Website { get; set; }
    }
}
