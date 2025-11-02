using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace QutieBot.Bot
{
    /// <summary>
    /// Handles welcome and leave messages for guild members
    /// </summary>
    public class WelcomeLeaveMessenger
    {
        private readonly ILogger<WelcomeLeaveMessenger> _logger;
        private const ulong WELCOME_CHANNEL_ID = 1137877083742289992;
        private const ulong GUILD_INFORMATION_CHANNEL_ID = 1137877575478288434;
        private const ulong GUILD_APPLICATION_CHANNEL_ID = 1197317680210915368;

        public WelcomeLeaveMessenger(ILogger<WelcomeLeaveMessenger> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Sends a welcome message when a member joins the guild
        /// </summary>
        public async Task SendWelcomeMessageAsync(DiscordClient client, GuildMemberAddedEventArgs e)
        {
            try
            {
                var channel = await client.GetChannelAsync(WELCOME_CHANNEL_ID);
                if (channel == null)
                {
                    _logger.LogWarning($"Welcome channel {WELCOME_CHANNEL_ID} not found");
                    return;
                }

                // Get channel mentions
                string guildInfoMention = GUILD_INFORMATION_CHANNEL_ID != 0
                    ? $"<#{GUILD_INFORMATION_CHANNEL_ID}>"
                    : "guild-information";

                string guildAppMention = GUILD_APPLICATION_CHANNEL_ID != 0
                    ? $"<#{GUILD_APPLICATION_CHANNEL_ID}>"
                    : "❗guild-application❗";

                // Create the embed
                var embed = new DiscordEmbedBuilder()
                    .WithTitle($"Welcome {e.Member.DisplayName}!")
                    .WithDescription($"We hope you enjoy your stay in Quintessence! Please head to {guildInfoMention} to learn of our principles. You may begin your application for any Quintessence roster by heading to {guildAppMention} & filling out the application form!")
                    .WithColor(DiscordColor.Purple)
                    .WithThumbnail(e.Member.AvatarUrl);

                // Send message with mention outside embed
                var messageBuilder = new DiscordMessageBuilder()
                    .WithContent($"Hello {e.Member.Mention} and welcome to Quintessence!")
                    .AddEmbed(embed);

                await channel.SendMessageAsync(messageBuilder);

                _logger.LogInformation($"Sent welcome message for {e.Member.Username} ({e.Member.Id})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending welcome message for user {e.Member.Username} ({e.Member.Id})");
            }
        }

        /// <summary>
        /// Sends a leave message when a member leaves the guild
        /// </summary>
        public async Task SendLeaveMessageAsync(DiscordClient client, GuildMemberRemovedEventArgs e)
        {
            try
            {
                var channel = await client.GetChannelAsync(WELCOME_CHANNEL_ID);
                if (channel == null)
                {
                    _logger.LogWarning($"Welcome channel {WELCOME_CHANNEL_ID} not found");
                    return;
                }

                // Create the embed with red color
                var embed = new DiscordEmbedBuilder()
                    .WithColor(DiscordColor.Red) 
                    .WithThumbnail(e.Member.AvatarUrl);

                // Send message with mention
                var messageBuilder = new DiscordMessageBuilder()
                    .WithContent($"It seems like {e.Member.Mention} left Quintessence!")
                    .AddEmbed(embed);

                await channel.SendMessageAsync(messageBuilder);

                _logger.LogInformation($"Sent leave message for {e.Member.Username} ({e.Member.Id})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending leave message for user {e.Member.Username} ({e.Member.Id})");
            }
        }
    }
}