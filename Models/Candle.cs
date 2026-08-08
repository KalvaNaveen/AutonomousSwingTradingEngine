using System;

namespace AutonomousTradingEngine.Models
{
    public class Candle
    {
        public DateTime Date { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
    }

    public class ScanCandidate
    {
        public string Ticker { get; set; } = string.Empty;
        public decimal LTP { get; set; }
        public decimal ATR { get; set; }
        public decimal RVOL { get; set; }
    }
}