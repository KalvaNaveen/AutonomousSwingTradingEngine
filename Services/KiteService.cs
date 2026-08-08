using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AutonomousTradingEngine.Services
{
    public class KiteService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<KiteService> _logger;

        public KiteService(HttpClient httpClient, ILogger<KiteService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _httpClient.BaseAddress = new Uri("https://api.kite.trade/");
        }

        public async Task<string?> PlaceMarketBuyOrderAsync(string apiKey, string accessToken, string symbol, int quantity)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-Kite-Version", "3");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", $"{apiKey}:{accessToken}");

            var payload = new Dictionary<string, string>
            {
                { "tradingsymbol", symbol },
                { "exchange", "NSE" },
                { "transaction_type", "BUY" },
                { "order_type", "MARKET" },
                { "quantity", quantity.ToString() },
                { "product", "CNC" },
                { "validity", "DAY" }
            };

            var response = await _httpClient.PostAsync("orders/regular", new FormUrlEncodedContent(payload));
            var json = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(json);
                string orderId = doc.RootElement.GetProperty("data").GetProperty("order_id").GetString()!;
                _logger.LogInformation($"Live Order Placed for {symbol}. Order ID: {orderId}");
                return orderId;
            }

            _logger.LogError($"Failed to place Live Order for {symbol}: {json}");
            return null;
        }

        public async Task<bool> PlaceGttStopLossAsync(string apiKey, string accessToken, string symbol, int quantity, decimal triggerPrice, decimal lastPrice)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-Kite-Version", "3");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", $"{apiKey}:{accessToken}");

            decimal limitPrice = Math.Round(triggerPrice - 2.0m, 1); // Buffer for market gaps

            var orderDetail = new
            {
                transaction_type = "SELL",
                quantity = quantity,
                product = "CNC",
                order_type = "LIMIT",
                price = limitPrice
            };

            var gttPayload = new
            {
                type = "single",
                condition = JsonSerializer.Serialize(new { exchange = "NSE", tradingsymbol = symbol, trigger_values = new[] { triggerPrice }, last_price = lastPrice }),
                orders = JsonSerializer.Serialize(new[] { orderDetail })
            };

            var content = new StringContent(JsonSerializer.Serialize(gttPayload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("gtt/triggers", content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation($"GTT 2x ATR Stop Loss placed successfully for {symbol} at ₹{triggerPrice}");
                return true;
            }

            var json = await response.Content.ReadAsStringAsync();
            _logger.LogError($"Failed to place GTT for {symbol}: {json}");
            return false;
        }
    }
}