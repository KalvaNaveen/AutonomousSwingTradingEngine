using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutonomousTradingEngine.Models
{
    public class TradeLog
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(20)]
        public string Ticker { get; set; } = string.Empty;

        [Required]
        [MaxLength(10)]
        public string Mode { get; set; } = "PAPER";

        public DateTime EntryDate { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "decimal(18,2)")]
        public decimal EntryPrice { get; set; }

        public int Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal StopLossPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal RvolAtEntry { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? ExitDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ExitPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PnL { get; set; }

        [MaxLength(100)]
        public string? ExitReason { get; set; }
    }
}