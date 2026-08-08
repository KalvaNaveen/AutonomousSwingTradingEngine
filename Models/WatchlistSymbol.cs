using System.ComponentModel.DataAnnotations;

namespace AutonomousTradingEngine.Models
{
    public class WatchlistSymbol
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [MaxLength(50)]
        public string Ticker { get; set; } = string.Empty;
    }
}