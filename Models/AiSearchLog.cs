using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductHub_MVC.Models
{
    [Table("T_AI_SEARCH_LOGS")]
    public class AiSearchLog
    {
        [Key]
        [Column("F_LOG_ID")]
        public int LogId { get; set; }

        [Required]
        [Column("F_SEARCH_QUERY")]
        public string SearchQuery { get; set; } = string.Empty;

        [Required]
        [Column("F_USER_SESSION")]
        public string UserSession { get; set; } = string.Empty;

        [Column("F_RESPONSE_TIME_MS")]
        public int ResponseTimeMs { get; set; }

        [Required]
        [Column("F_MODEL_USED")]
        public string ModelUsed { get; set; } = string.Empty;

        [Column("F_TIMESTAMP")]
        public DateTime Timestamp { get; set; } = DateTime.Now;

        [Column("F_RESULT_COUNT")]
        public int ResultCount { get; set; }
    }
}
