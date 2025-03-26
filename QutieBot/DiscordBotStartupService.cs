using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace QutieBot
{
    /// <summary>
    /// Hosted service that handles Discord bot startup and lifecycle
    /// </summary>
    public class DiscordBotStartupService : IHostedService
    {
        private readonly DiscordClient _client;
        private readonly Webhook _webhook;
        private readonly ILogger<DiscordBotStartupService> _logger;

        public DiscordBotStartupService(
            DiscordClient client,
            Webhook webhook,
            ILogger<DiscordBotStartupService> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _webhook = webhook ?? throw new ArgumentNullException(nameof(webhook));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting Discord bot");

                // Start the webhook service
                _logger.LogInformation("Starting webhook service");
                await _webhook.StartAsync(cancellationToken);

                // Connect the Discord client
                _logger.LogInformation("Connecting Discord client");
                DiscordActivity status = new("over Quintessence", DiscordActivityType.Watching);
                await _client.ConnectAsync(status, DiscordUserStatus.Online);

                _logger.LogInformation("Discord bot startup completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Error during Discord bot startup");
                throw;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Stopping Discord bot");

                // Disconnect the Discord client
                await _client.DisconnectAsync();

                // Stop the webhook service
                await _webhook.StopAsync(cancellationToken);

                _logger.LogInformation("Discord bot stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Discord bot shutdown");
            }
        }
    }
}