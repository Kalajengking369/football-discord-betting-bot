using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace Discord.Services
{
    /// <summary>
    /// Hosted service managing the Discord bot client lifecycle, interaction routing, and slash command registration.
    /// </summary>
    public class DiscordBotService : IHostedService
    {
        private readonly DiscordSocketClient _client;
        private readonly InteractionService _interactions;
        private readonly IServiceProvider _services;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DiscordBotService> _logger;

        public DiscordBotService(
            DiscordSocketClient client,
            InteractionService interactions,
            IServiceProvider services,
            IConfiguration configuration,
            ILogger<DiscordBotService> logger)
        {
            _client = client;
            _interactions = interactions;
            _services = services;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _client.Log += LogAsync;
            _client.Ready += ReadyAsync;
            _client.InteractionCreated += HandleInteraction;

            var token = _configuration["DiscordToken"];
            if (string.IsNullOrEmpty(token) || token.Contains("YOUR_DISCORD_BOT_TOKEN"))
            {
                _logger.LogError("Discord Token is missing or not configured! Please set DiscordToken in configuration or environment variables.");
                return;
            }

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Discord bot service...");
            try
            {
                await _client.StopAsync();
                await _client.LogoutAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error occurred during Discord client shutdown.");
            }
        }

        private async Task HandleInteraction(SocketInteraction interaction)
        {
            try
            {
                var context = new SocketInteractionContext(_client, interaction);
                await _interactions.ExecuteCommandAsync(context, _services);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling Discord interaction.");
                if (interaction.Type == InteractionType.ApplicationCommand)
                {
                    try
                    {
                        var originalResponse = await interaction.GetOriginalResponseAsync();
                        await originalResponse.DeleteAsync();
                    }
                    catch
                    {
                        // Ignore cleanup failure on errored interaction
                    }
                }
            }
        }

        private Task LogAsync(LogMessage log)
        {
            _logger.LogInformation("{LogMessage}", log.ToString());
            return Task.CompletedTask;
        }

        private async Task ReadyAsync()
        {
            _logger.LogInformation("Bot is ready and connected as {User}", _client.CurrentUser.Username);
            
            // Register interaction modules from current assembly
            await _interactions.AddModulesAsync(typeof(DiscordBotService).Assembly, _services);

            // Register application commands globally across Discord
            await _interactions.RegisterCommandsGloballyAsync();
            _logger.LogInformation("Slash commands successfully registered globally.");
        }
    }
}
