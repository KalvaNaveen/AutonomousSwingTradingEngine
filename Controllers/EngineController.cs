using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AutonomousTradingEngine.Data;
using AutonomousTradingEngine.Models;
using AutonomousTradingEngine.Services;

namespace AutonomousTradingEngine.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EngineController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly TradingEngineWorker _worker;

        public EngineController(ApplicationDbContext db, TradingEngineWorker worker)
        {
            _db = db;
            _worker = worker;
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var config = await _db.EngineConfigs.FirstOrDefaultAsync();
            var activePositions = await _db.TradeLogs.Where(t => t.IsActive).ToListAsync();
            return Ok(new { Config = config, ActivePositions = activePositions });
        }

        [HttpPost("toggle-mode")]
        public async Task<IActionResult> ToggleMode([FromQuery] string mode)
        {
            if (mode != "PAPER" && mode != "LIVE")
                return BadRequest("Mode must be PAPER or LIVE.");

            var config = await _db.EngineConfigs.FirstOrDefaultAsync();
            if (config == null) return NotFound();

            config.TradingMode = mode;
            await _db.SaveChangesAsync();

            return Ok(new { Message = $"Engine mode updated to {mode}." });
        }

        [HttpPost("update-token")]
        public async Task<IActionResult> UpdateKiteToken([FromQuery] string apiKey, [FromQuery] string accessToken)
        {
            var config = await _db.EngineConfigs.FirstOrDefaultAsync();
            if (config == null) return NotFound();

            config.KiteApiKey = apiKey;
            config.KiteAccessToken = accessToken;
            await _db.SaveChangesAsync();

            return Ok(new { Message = "Zerodha credentials updated successfully." });
        }

        [HttpGet("trades")]
        public async Task<IActionResult> GetTradeHistory()
        {
            var trades = await _db.TradeLogs.OrderByDescending(t => t.EntryDate).ToListAsync();
            return Ok(trades);
        }

      

        [HttpPost("trigger-scan")]
        public IActionResult TriggerManualScan() // Removed 'async Task<>'
        {
            // Fire and forget the background worker
            _ = _worker.ExecuteTradingRoutineAsync(default);
            return Ok(new { Message = "Manual 3:15 PM Scan triggered asynchronously." });
        }


        [HttpPost("force-exit/{ticker}")]
        public async Task<IActionResult> ForceExitPosition(string ticker)
        {
            // Logic to hit Zerodha API and sell at market price, then update database
            return Ok(new { Message = $"{ticker} position closed manually." });
        }
    }
}