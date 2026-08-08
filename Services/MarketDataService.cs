using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AutonomousTradingEngine.Models;

namespace AutonomousTradingEngine.Services
{
    public class MarketDataService
    {
        private readonly HttpClient _httpClient;

        public MarketDataService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
        }

        public async Task<List<Candle>> GetHistoricalDataAsync(string symbol)
        {
            string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}.NS?interval=1d&range=6mo";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return new List<Candle>();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            var timestamps = result.GetProperty("timestamp").EnumerateArray().ToList();
            var quote = result.GetProperty("indicators").GetProperty("quote")[0];

            var opens = quote.GetProperty("open").EnumerateArray().ToList();
            var highs = quote.GetProperty("high").EnumerateArray().ToList();
            var lows = quote.GetProperty("low").EnumerateArray().ToList();
            var closes = quote.GetProperty("close").EnumerateArray().ToList();
            var volumes = quote.GetProperty("volume").EnumerateArray().ToList();

            var candles = new List<Candle>();

            for (int i = 0; i < timestamps.Count; i++)
            {
                if (closes[i].ValueKind == JsonValueKind.Null || volumes[i].ValueKind == JsonValueKind.Null) continue;

                candles.Add(new Candle
                {
                    Date = DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64()).DateTime,
                    Open = opens[i].GetDecimal(),
                    High = highs[i].GetDecimal(),
                    Low = lows[i].GetDecimal(),
                    Close = closes[i].GetDecimal(),
                    Volume = volumes[i].GetInt64()
                });
            }

            return candles;
        }

        public ScanCandidate? EvaluateStrategy(string symbol, List<Candle> candles)
        {
            if (candles.Count < 30) return null;

            int n = candles.Count;
            var closes = candles.Select(c => c.Close).ToList();
            
            var ema10 = CalculateEMA(closes, 10);
            var ema20 = CalculateEMA(closes, 20);
            var atr10 = CalculateATR(candles, 10);

            // Calculate RVOL (Current Volume / 20-day Volume SMA)
            decimal avgVol20 = (decimal)candles.Skip(n - 21).Take(20).Average(c => c.Volume);
            decimal currentVol = candles[n - 1].Volume;
            decimal rvol = avgVol20 > 0 ? currentVol / avgVol20 : 0;

            // Signal Check: 10 EMA crossed above 20 EMA today & Price > 10 EMA
            bool crossUp = (ema10[n - 1] > ema20[n - 1]) && (ema10[n - 2] <= ema20[n - 2]);
            bool priceAboveEma = closes[n - 1] > ema10[n - 1];

            if (crossUp && priceAboveEma)
            {
                return new ScanCandidate
                {
                    Ticker = symbol,
                    LTP = closes[n - 1],
                    ATR = atr10[n - 1],
                    RVOL = rvol
                };
            }

            return null;
        }

        private List<decimal> CalculateEMA(List<decimal> values, int period)
        {
            var ema = new decimal[values.Count];
            decimal k = 2m / (period + 1);

            ema[0] = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                if (i < period)
                {
                    ema[i] = values.Take(i + 1).Average();
                }
                else
                {
                    ema[i] = (values[i] * k) + (ema[i - 1] * (1m - k));
                }
            }
            return ema.ToList();
        }

        private List<decimal> CalculateATR(List<Candle> candles, int period)
        {
            var trList = new List<decimal> { candles[0].High - candles[0].Low };

            for (int i = 1; i < candles.Count; i++)
            {
                decimal tr = Math.Max(
                    candles[i].High - candles[i].Low,
                    Math.Max(
                        Math.Abs(candles[i].High - candles[i - 1].Close),
                        Math.Abs(candles[i].Low - candles[i - 1].Close)
                    )
                );
                trList.Add(tr);
            }

            var atr = new decimal[candles.Count];
            for (int i = 0; i < candles.Count; i++)
            {
                if (i < period)
                    atr[i] = trList.Take(i + 1).Average();
                else
                    atr[i] = ((atr[i - 1] * (period - 1)) + trList[i]) / period;
            }

            return atr.ToList();
        }
    }
}