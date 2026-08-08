using System;
using System.Collections.Generic;

namespace AutonomousTradingEngine.Models
{
    public class BacktestRequest
    {
        public DateTime StartDate { get; set; } = new DateTime(2020, 1, 1);
        public DateTime EndDate { get; set; } = DateTime.UtcNow;
        public decimal InitialCapital { get; set; } = 1500000m;
        public int MaxPositions { get; set; } = 5;
        public decimal AtrMultiplier { get; set; } = 2.0m;
    }

    public class EquityPoint
    {
        public DateTime Date { get; set; }
        public decimal TotalEquity { get; set; }
    }

    public class BacktestResult
    {
        public decimal InitialCapital { get; set; }
        public decimal FinalEquity { get; set; }
        public decimal TotalReturnPercent { get; set; }
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public decimal WinRatePercent { get; set; }
        public decimal MaxDrawdownPercent { get; set; }
        public List<TradeLog> TradeLog { get; set; } = new();
        public List<EquityPoint> EquityCurve { get; set; } = new();
    }
}