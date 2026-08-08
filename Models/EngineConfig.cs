using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutonomousTradingEngine.Models
{
    public class EngineConfig
    {
        [Key]
        public int Id { get; set; } = 1;

        [Required]
        [MaxLength(10)]
        public string TradingMode { get; set; } = "PAPER"; // "PAPER" or "LIVE"

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalCapital { get; set; } = 1500000m;

        public int MaxPositions { get; set; } = 5;

        [Required]
        [MaxLength(10)]
        public string AllocationStyle { get; set; } = "FIXED"; // "FIXED" or "DYNAMIC"

        [Column(TypeName = "decimal(18,2)")]
        public decimal FixedAllocationAmount { get; set; } = 300000m;

        [MaxLength(200)]
        public string? KiteApiKey { get; set; }

        [MaxLength(200)]
        public string? KiteAccessToken { get; set; }
    }
}