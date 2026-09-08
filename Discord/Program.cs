using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Discord.Services;
using Discord.Data;

var builder = Host.CreateApplicationBuilder(args);

// Configuration
builder.Configuration.SetBasePath(AppContext.BaseDirectory);
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=betting_bot.db";

// Core Domain Services
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddSingleton<IScoringService, ScoringService>();

// Discord Services
builder.Services.AddSingleton(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.Guilds,
    AlwaysDownloadUsers = true
});
builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()));
builder.Services.AddHttpClient<FootballApiService>();

// Hosted Services (Managed lifecycle)
builder.Services.AddHostedService<DiscordBotService>();
builder.Services.AddHostedService<MatchSyncWorker>();

var host = builder.Build();

// Ensure database schema is ready
using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();
    
    // SQLite schema evolution safety checks
    try { dbContext.Database.ExecuteSqlRaw("ALTER TABLE Matches ADD COLUMN Winner TEXT NULL;"); } catch {}
    try { dbContext.Database.ExecuteSqlRaw("ALTER TABLE Matches ADD COLUMN HomeStandardScore INTEGER NULL;"); } catch {}
    try { dbContext.Database.ExecuteSqlRaw("ALTER TABLE Matches ADD COLUMN AwayStandardScore INTEGER NULL;"); } catch {}
    try { dbContext.Database.ExecuteSqlRaw("ALTER TABLE Bets ADD COLUMN PredictedWinner INTEGER NULL;"); } catch {}
    try { dbContext.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS BotSettings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);"); } catch {}
}

// Run the host
await host.RunAsync();
