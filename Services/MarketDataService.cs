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
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        }

        public async Task<List<Candle>> GetHistoricalDataAsync(string ticker, DateTime? startDate = null, DateTime? endDate = null)
        {
            // Sanitize ticker to guarantee a clean, single .NS suffix
            string formattedTicker = FormatYahooTicker(ticker);
            if (string.IsNullOrEmpty(formattedTicker)) return new List<Candle>();

            string url;
            if (startDate.HasValue && endDate.HasValue)
            {
                // Convert UI Start/End dates to Unix Timestamps
                long period1 = ((DateTimeOffset)startDate.Value).ToUnixTimeSeconds();
                long period2 = ((DateTimeOffset)endDate.Value).ToUnixTimeSeconds();
                url = $"https://query1.finance.yahoo.com/v8/finance/chart/{formattedTicker}?interval=1d&period1={period1}&period2={period2}";
            }
            else
            {
                // Default to 2-year range for live daily 3:15 PM scans
                url = $"https://query1.finance.yahoo.com/v8/finance/chart/{formattedTicker}?interval=1d&range=2y";
            }

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return new List<Candle>();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                // Safe extraction of chart result
                if (!doc.RootElement.TryGetProperty("chart", out var chart) ||
                    !chart.TryGetProperty("result", out var resultArr) ||
                    resultArr.ValueKind == JsonValueKind.Null ||
                    resultArr.GetArrayLength() == 0)
                {
                    return new List<Candle>();
                }

                var result = resultArr[0];
                if (!result.TryGetProperty("timestamp", out var timestampProp) ||
                    !result.TryGetProperty("indicators", out var indicatorsProp) ||
                    !indicatorsProp.TryGetProperty("quote", out var quoteArr) ||
                    quoteArr.GetArrayLength() == 0)
                {
                    return new List<Candle>();
                }

                var timestamps = timestampProp.EnumerateArray().ToList();
                var quote = quoteArr[0];

                if (!quote.TryGetProperty("open", out var opensProp) ||
                    !quote.TryGetProperty("high", out var highsProp) ||
                    !quote.TryGetProperty("low", out var lowsProp) ||
                    !quote.TryGetProperty("close", out var closesProp) ||
                    !quote.TryGetProperty("volume", out var volumesProp))
                {
                    return new List<Candle>();
                }

                var opens = opensProp.EnumerateArray().ToList();
                var highs = highsProp.EnumerateArray().ToList();
                var lows = lowsProp.EnumerateArray().ToList();
                var closes = closesProp.EnumerateArray().ToList();
                var volumes = volumesProp.EnumerateArray().ToList();

                var candles = new List<Candle>();

                for (int i = 0; i < timestamps.Count; i++)
                {
                    if (i >= opens.Count || i >= highs.Count || i >= lows.Count || i >= closes.Count || i >= volumes.Count)
                        break;

                    // Skip candles with missing/null pricing data
                    if (closes[i].ValueKind == JsonValueKind.Null ||
                        volumes[i].ValueKind == JsonValueKind.Null ||
                        opens[i].ValueKind == JsonValueKind.Null ||
                        highs[i].ValueKind == JsonValueKind.Null ||
                        lows[i].ValueKind == JsonValueKind.Null)
                    {
                        continue;
                    }

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
            catch
            {
                // Return empty list on network or JSON failure to keep server processing alive
                return new List<Candle>();
            }
        }

        public ScanCandidate? EvaluateStrategy(string symbol, List<Candle> candles)
        {
            if (candles == null || candles.Count < 30) return null;

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

        public static string FormatYahooTicker(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker)) return string.Empty;

            string clean = ticker.Trim().ToUpper();

            // Strip all trailing .NS instances regardless of case or duplication (.NS.NS)
            while (clean.EndsWith(".NS"))
            {
                clean = clean.Substring(0, clean.Length - 3);
            }

            return $"{clean}.NS";
        }
    }
}