using AutonomousTradingEngine.Data;
using AutonomousTradingEngine.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Add Controllers and HTTP Clients
builder.Services.AddControllers();
builder.Services.AddHttpClient<MarketDataService>();
builder.Services.AddHttpClient<KiteService>();

// 2. Resolve and Convert Connection String from Render Environment Variables
var rawConnectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? builder.Configuration["DATABASE_URL"]
    ?? throw new InvalidOperationException("Database connection string 'ConnectionStrings__DefaultConnection' is missing from Environment Variables.");

string connectionString = ConvertPostgresUrlToConnectionString(rawConnectionString);

// 3. Configure PostgreSQL with EF Core
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// 4. Register Engine Background Worker
builder.Services.AddSingleton<TradingEngineWorker>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<TradingEngineWorker>());

var app = builder.Build();

// 5. Auto-Run EF Core Migrations on Startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.UseRouting();
app.MapControllers();

app.Run();

// --- HELPER FUNCTION: Convert postgres:// URI to Npgsql Key-Value String ---
static string ConvertPostgresUrlToConnectionString(string rawUrl)
{
    if (!rawUrl.StartsWith("postgres://") && !rawUrl.StartsWith("postgresql://"))
    {
        return rawUrl; // Already in Host=...;Database=... format
    }

    var uri = new Uri(rawUrl);
    var userInfo = uri.UserInfo.Split(':');
    var host = uri.Host;
    var port = uri.Port > 0 ? uri.Port : 5432;
    var database = uri.AbsolutePath.TrimStart('/');
    var username = userInfo[0];
    var password = userInfo.Length > 1 ? userInfo[1] : "";

    return $"Host={host};Port={port};Database={database};Username={username};Password={password};Ssl Mode=Prefer;";
}