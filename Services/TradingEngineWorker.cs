using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AutonomousTradingEngine.Data;
using AutonomousTradingEngine.Models;

namespace AutonomousTradingEngine.Services
{
    public class TradingEngineWorker : BackgroundService
    {
        private readonly ILogger<TradingEngineWorker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly MarketDataService _marketData;
        private readonly KiteService _kiteService;
        private readonly TimeZoneInfo _istZone;

        public TradingEngineWorker(
            ILogger<TradingEngineWorker> logger, 
            IServiceScopeFactory scopeFactory,
            MarketDataService marketData,
            KiteService kiteService)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _marketData = marketData;
            _kiteService = kiteService;
            _istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Autonomous Trading Engine Initialized.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _istZone);

                // Run at 3:15 PM IST on Monday-Friday
                if (nowIst.Hour == 15 && nowIst.Minute == 15 && nowIst.DayOfWeek != DayOfWeek.Saturday && nowIst.DayOfWeek != DayOfWeek.Sunday)
                {
                    _logger.LogInformation("3:15 PM IST Alarm Triggered. Starting Scan Routine...");
                    await ExecuteTradingRoutineAsync(stoppingToken);
                    await Task.Delay(TimeSpan.FromSeconds(61), stoppingToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        public async Task ExecuteTradingRoutineAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var config = db.EngineConfigs.FirstOrDefault();
            if (config == null) return;

            int activePositions = db.TradeLogs.Count(t => t.IsActive);
            int availableSlots = config.MaxPositions - activePositions;

            if (availableSlots <= 0)
            {
                _logger.LogInformation("Max position slots (5) reached. Skipping scan.");
                return;
            }

            // Load Master Watchlist
            var watchlist = GetMasterWatchlist();
            var candidates = new List<ScanCandidate>();

            foreach (var ticker in watchlist)
            {
                var candles = await _marketData.GetHistoricalDataAsync(ticker);
                var candidate = _marketData.EvaluateStrategy(ticker, candles);
                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }

            // Sort by Institutional Volume (RVOL) Tie-Breaker
            var selectedTrades = candidates.OrderByDescending(c => c.RVOL).Take(availableSlots).ToList();

            foreach (var trade in selectedTrades)
            {
                decimal allocation = config.AllocationStyle == "FIXED" 
                    ? config.FixedAllocationAmount 
                    : config.TotalCapital / config.MaxPositions;

                int qty = (int)(allocation / trade.LTP);
                if (qty <= 0) continue;

                decimal stopLoss = Math.Round(trade.LTP - (2m * trade.ATR), 2);

                _logger.LogInformation($"[BUY SIGNAL] {trade.Ticker} | Price: ₹{trade.LTP} | Qty: {qty} | 2x ATR SL: ₹{stopLoss} | RVOL: {trade.RVOL:F2}x");

                if (config.TradingMode == "PAPER")
                {
                    db.TradeLogs.Add(new TradeLog
                    {
                        Ticker = trade.Ticker,
                        Mode = "PAPER",
                        EntryDate = DateTime.UtcNow,
                        EntryPrice = trade.LTP,
                        Quantity = qty,
                        StopLossPrice = stopLoss,
                        RvolAtEntry = trade.RVOL,
                        IsActive = true
                    });
                }
                else if (config.TradingMode == "LIVE" && !string.IsNullOrEmpty(config.KiteApiKey) && !string.IsNullOrEmpty(config.KiteAccessToken))
                {
                    string? orderId = await _kiteService.PlaceMarketBuyOrderAsync(config.KiteApiKey, config.KiteAccessToken, trade.Ticker, qty);
                    if (orderId != null)
                    {
                        await _kiteService.PlaceGttStopLossAsync(config.KiteApiKey, config.KiteAccessToken, trade.Ticker, qty, stopLoss, trade.LTP);

                        db.TradeLogs.Add(new TradeLog
                        {
                            Ticker = trade.Ticker,
                            Mode = "LIVE",
                            EntryDate = DateTime.UtcNow,
                            EntryPrice = trade.LTP,
                            Quantity = qty,
                            StopLossPrice = stopLoss,
                            RvolAtEntry = trade.RVOL,
                            IsActive = true
                        });
                    }
                }

                await db.SaveChangesAsync(cancellationToken);
            }
        }

        private List<string> GetMasterWatchlist()
        {
            // Fallback list of key momentum universe tickers if local file is absent
            return new List<string> { "BSE", "BHARATFORG", "FEDERALBNK", "GLENMARK", "KEI", "MCX", "NATIONALUM", "POLYCAB", "ABB", "ADANIPOWER" };
        }
    }
}