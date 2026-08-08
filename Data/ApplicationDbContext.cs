using Microsoft.EntityFrameworkCore;
using AutonomousTradingEngine.Models;

namespace AutonomousTradingEngine.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) 
            : base(options)
        {
        }

        public DbSet<EngineConfig> EngineConfigs { get; set; } = null!;
        public DbSet<TradeLog> TradeLogs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<EngineConfig>().HasData(
                new EngineConfig
                {
                    Id = 1,
                    TradingMode = "PAPER",
                    TotalCapital = 1500000m,
                    MaxPositions = 5,
                    AllocationStyle = "FIXED",
                    FixedAllocationAmount = 300000m
                }
            );
        }
    }
}