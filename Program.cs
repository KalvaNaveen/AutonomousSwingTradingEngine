using AutonomousTradingEngine.Data;
using AutonomousTradingEngine.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Add Controllers and HTTP Clients
builder.Services.AddControllers();
builder.Services.AddHttpClient<MarketDataService>();
builder.Services.AddHttpClient<KiteService>();

// 2. Resolve Connection String from Environment Variables
var rawConnectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? builder.Configuration["DATABASE_URL"]
    ?? throw new InvalidOperationException("Database connection string missing.");

string connectionString = ConvertPostgresUrlToConnectionString(rawConnectionString);

// 3. Configure PostgreSQL with EF Core
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// 4. Register Engine Background Worker
builder.Services.AddSingleton<TradingEngineWorker>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<TradingEngineWorker>());

var app = builder.Build();

// 5. Enable Detailed Exception Pages for Debugging
app.UseDeveloperExceptionPage();

// 6. Auto-Run EF Core Migrations on Startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.UseRouting();
app.MapControllers();

app.Run();

// --- HELPER FUNCTION: Convert postgres:// URI to Npgsql String with SSL Trust ---
static string ConvertPostgresUrlToConnectionString(string rawUrl)
{
    if (!rawUrl.StartsWith("postgres://") && !rawUrl.StartsWith("postgresql://"))
    {
        return rawUrl;
    }

    var uri = new Uri(rawUrl);
    var userInfo = uri.UserInfo.Split(':');
    var host = uri.Host;
    var port = uri.Port > 0 ? uri.Port : 5432;
    var database = uri.AbsolutePath.TrimStart('/');
    var username = userInfo[0];
    var password = userInfo.Length > 1 ? userInfo[1] : "";

    // Added 'Ssl Mode=Require;Trust Server Certificate=true;' for Render External Database compatibility
    return $"Host={host};Port={port};Database={database};Username={username};Password={password};Ssl Mode=Require;Trust Server Certificate=true;";
}