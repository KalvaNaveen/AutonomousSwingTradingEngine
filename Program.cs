using AutonomousTradingEngine.Data;
using AutonomousTradingEngine.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add Controllers and HTTP Clients
builder.Services.AddControllers();
builder.Services.AddHttpClient<MarketDataService>();
builder.Services.AddHttpClient<KiteService>();

// Configure PostgreSQL with EF Core
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Database=momentum_db;Username=postgres;Password=postgres";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register Engine Background Worker as a Singleton & HostedService
builder.Services.AddSingleton<TradingEngineWorker>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<TradingEngineWorker>());

var app = builder.Build();

// Automatically apply EF Core database migrations on boot
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.UseRouting();
app.MapControllers();

app.Run();