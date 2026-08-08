using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutonomousTradingEngine.Data;
using AutonomousTradingEngine.Models;
using Microsoft.EntityFrameworkCore;

namespace AutonomousTradingEngine.Services
{
    public class BacktestService
    {
        private readonly ApplicationDbContext _db;
        private readonly MarketDataService _marketData;

        public BacktestService(ApplicationDbContext db, MarketDataService marketData)
        {
            _db = db;
            _marketData = marketData;
        }

        public async Task<BacktestResult> RunBacktestAsync(BacktestRequest request)
        {
            var watchlist = await _db.WatchlistSymbols.Select(w => w.Ticker).ToListAsync();
            if (!watchlist.Any())
            {
                watchlist = new List<string> { "BSE", "BHARATFORG", "FEDERALBNK", "GLENMARK", "KEI", "MCX", "NATIONALUM", "POLYCAB", "ABB", "ADANIPOWER" };
            }

            // Fetch historical candle series for all universe symbols
            var symbolData = new Dictionary<string, List<Candle>>();
            foreach (var sym in watchlist)
            {
                string ticker = sym.EndsWith(".NS") ? sym : $"{sym}.NS";
                var candles = await _marketData.GetHistoricalDataAsync(ticker);
                if (candles.Count >= 50)
                {
                    symbolData[sym] = candles.Where(c => c.Date >= request.StartDate && c.Date <= request.EndDate).ToList();
                }
            }

            // Extract all unique trading dates sorted chronologically
            var allDates = symbolData.Values
                .SelectMany(c => c.Select(x => x.Date.Date))
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            decimal cash = request.InitialCapital;
            var openPositions = new Dictionary<string, TradeLog>();
            var tradeHistory = new List<TradeLog>();
            var equityCurve = new List<EquityPoint>();

            decimal peakEquity = request.InitialCapital;
            decimal maxDrawdown = 0m;

            foreach (var currentDate in allDates)
            {
                // 1. Process Exits
                foreach (var ticker in openPositions.Keys.ToList())
                {
                    var candles = symbolData[ticker];
                    var today = candles.FirstOrDefault(c => c.Date.Date == currentDate);
                    if (today == null) continue;

                    var pos = openPositions[ticker];
                    decimal? exitPrice = null;
                    string exitReason = "";

                    // Stop Loss Triggered (2x ATR)
                    if (today.Low <= pos.StopLossPrice)
                    {
                        exitPrice = Math.Min(today.Open, pos.StopLossPrice); // Account for gap slippage
                        exitReason = "2x ATR Stop Loss Hit";
                    }

                    if (exitPrice.HasValue)
                    {
                        pos.ExitDate = currentDate;
                        pos.ExitPrice = exitPrice.Value;
                        pos.PnL = (exitPrice.Value - pos.EntryPrice) * pos.Quantity;
                        pos.IsActive = false;
                        pos.ExitReason = exitReason;

                        cash += (exitPrice.Value * pos.Quantity);
                        tradeHistory.Add(pos);
                        openPositions.Remove(ticker);
                    }
                }

                // Calculate Portfolio Valuation
                decimal openValue = openPositions.Sum(p =>
                {
                    var lastCandle = symbolData[p.Key].FirstOrDefault(c => c.Date.Date == currentDate);
                    return (lastCandle?.Close ?? p.Value.EntryPrice) * p.Value.Quantity;
                });

                decimal totalValuation = cash + openValue;
                if (totalValuation > peakEquity) peakEquity = totalValuation;
                decimal drawdown = peakEquity > 0 ? (peakEquity - totalValuation) / peakEquity * 100m : 0m;
                if (drawdown > maxDrawdown) maxDrawdown = drawdown;

                equityCurve.Add(new EquityPoint { Date = currentDate, TotalEquity = Math.Round(totalValuation, 2) });

                // 2. Process Entries
                if (openPositions.Count < request.MaxPositions)
                {
                    decimal slotAllocation = totalValuation / request.MaxPositions;
                    var candidates = new List<ScanCandidate>();

                    foreach (var kvp in symbolData)
                    {
                        string ticker = kvp.Key;
                        if (openPositions.ContainsKey(ticker)) continue;

                        var candles = kvp.Value;
                        int idx = candles.FindIndex(c => c.Date.Date == currentDate);
                        if (idx < 30) continue;

                        var candleSubset = candles.Take(idx + 1).ToList();
                        var candidate = _marketData.EvaluateStrategy(ticker, candleSubset);
                        if (candidate != null) candidates.Add(candidate);
                    }

                    int availableSlots = request.MaxPositions - openPositions.Count;
                    var topSetups = candidates.OrderByDescending(c => c.RVOL).Take(availableSlots);

                    foreach (var setup in topSetups)
                    {
                        int qty = (int)(slotAllocation / setup.LTP);
                        if (qty > 0 && cash >= (qty * setup.LTP))
                        {
                            cash -= (qty * setup.LTP);
                            decimal sl = setup.LTP - (request.AtrMultiplier * setup.ATR);

                            openPositions[setup.Ticker] = new TradeLog
                            {
                                Ticker = setup.Ticker,
                                Mode = "BACKTEST",
                                EntryDate = currentDate,
                                EntryPrice = setup.LTP,
                                Quantity = qty,
                                StopLossPrice = sl,
                                RvolAtEntry = setup.RVOL,
                                IsActive = true
                            };
                        }
                    }
                }
            }

            int wins = tradeHistory.Count(t => t.PnL > 0);
            int losses = tradeHistory.Count(t => t.PnL <= 0);
            decimal finalVal = equityCurve.LastOrDefault()?.TotalEquity ?? request.InitialCapital;

            return new BacktestResult
            {
                InitialCapital = request.InitialCapital,
                FinalEquity = finalVal,
                TotalReturnPercent = Math.Round(((finalVal / request.InitialCapital) - 1m) * 100m, 2),
                TotalTrades = tradeHistory.Count,
                WinningTrades = wins,
                LosingTrades = losses,
                WinRatePercent = tradeHistory.Count > 0 ? Math.Round((decimal)wins / tradeHistory.Count * 100m, 2) : 0m,
                MaxDrawdownPercent = Math.Round(maxDrawdown, 2),
                TradeLog = tradeHistory,
                EquityCurve = equityCurve
            };
        }
    }
}