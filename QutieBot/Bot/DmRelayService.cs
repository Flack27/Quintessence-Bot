using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace QutieBot.Bot
{
    /// <summary>
    /// Relays DMs between users and a staff channel
    /// </summary>
    public class DmRelayService
    {
        private readonly ILogger<DmRelayService> _logger;
        private const ulong DM_RELAY_CHANNEL_ID = 1140431266664153219; 

        public DmRelayService(ILogger<DmRelayService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Handles incoming DM messages and relays them to the staff channel
        /// </summary>
        public async Task HandleDmAsync(DiscordClient client, MessageCreatedEventArgs e)
        {
            try
            {
                // Skip if message is from a bot
                if (e.Author.IsBot)
                    return;

                // Only process DMs (no guild)
                if (e.Guild != null)
                    return;

                _logger.LogInformation($"Received DM from {e.Author.Username} ({e.Author.Id}): {e.Message.Content}");

                // Get the relay channel
                var relayChannel = await client.GetChannelAsync(DM_RELAY_CHANNEL_ID);
                if (relayChannel == null)
                {
                    _logger.LogWarning($"DM relay channel {DM_RELAY_CHANNEL_ID} not found");
                    return;
                }

                // Create embed for the relayed message
                var embed = new DiscordEmbedBuilder()
                    .WithAuthor($"{e.Author.Username} ({e.Author.Id})", iconUrl: e.Author.AvatarUrl)
                    .WithDescription(e.Message.Content)
                    .WithColor(DiscordColor.Blurple)
                    .WithTimestamp(DateTime.UtcNow)
                    .WithFooter("Reply to this message to respond to the user");

                // Add attachments if any
                if (e.Message.Attachments.Count > 0)
                {
                    var attachmentList = string.Join("\n", e.Message.Attachments.Select(a => $"[{a.FileName}]({a.Url})"));
                    embed.AddField("Attachments", attachmentList);
                }

                var messageBuilder = new DiscordMessageBuilder()
                    .WithContent($"**New DM from** {e.Author.Mention}")
                    .AddEmbed(embed);

                // Send to relay channel
                await relayChannel.SendMessageAsync(messageBuilder);

                _logger.LogInformation($"Relayed DM from {e.Author.Username} to channel {DM_RELAY_CHANNEL_ID}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error relaying DM from user {e.Author.Id}");
            }
        }

        /// <summary>
        /// Handles replies in the relay channel and sends them back to users
        /// </summary>
        public async Task HandleReplyAsync(DiscordClient client, MessageCreatedEventArgs e)
        {
            try
            {
                // Skip if message is from a bot
                if (e.Author.IsBot)
                    return;

                // Only process messages in the relay channel
                if (e.Channel.Id != DM_RELAY_CHANNEL_ID)
                    return;

                // Check if this is a reply to another message
                if (e.Message.ReferencedMessage == null)
                    return;

                _logger.LogInformation($"Staff {e.Author.Username} replying to DM in relay channel");

                // Get the original relayed message
                var referencedMessage = e.Message.ReferencedMessage;

                // Extract user ID from the referenced message
                // The format is: "**New DM from** <@USER_ID>"
                var content = referencedMessage.Content;
                var mentionStart = content.IndexOf("<@");
                var mentionEnd = content.IndexOf(">", mentionStart);

                if (mentionStart == -1 || mentionEnd == -1)
                {
                    _logger.LogWarning("Could not extract user ID from referenced message");
                    await e.Message.CreateReactionAsync(DiscordEmoji.FromName(client, ":x:"));
                    return;
                }

                var userIdStr = content.Substring(mentionStart + 2, mentionEnd - mentionStart - 2);
                
                // Remove '!' if it's a nickname mention
                userIdStr = userIdStr.Replace("!", "");

                if (!ulong.TryParse(userIdStr, out ulong userId))
                {
                    _logger.LogWarning($"Could not parse user ID: {userIdStr}");
                    await e.Message.CreateReactionAsync(DiscordEmoji.FromName(client, ":x:"));
                    return;
                }

                // Get the user
                var user = await client.GetUserAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning($"User {userId} not found");
                    await e.Message.CreateReactionAsync(DiscordEmoji.FromName(client, ":x:"));
                    return;
                }

                // Send DM to user (just the message content, as if the bot sent it)
                try
                {
                    var dmChannel = await user.CreateDmChannelAsync();
                    await dmChannel.SendMessageAsync(e.Message.Content);

                    // React to confirm success
                    await e.Message.CreateReactionAsync(DiscordEmoji.FromName(client, ":white_check_mark:"));

                    _logger.LogInformation($"Sent reply from {e.Author.Username} to user {userId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to send DM to user {userId}");
                    await e.Message.CreateReactionAsync(DiscordEmoji.FromName(client, ":x:"));
                    await e.Channel.SendMessageAsync($"❌ Failed to send message to {user.Mention}. They may have DMs disabled.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling reply in relay channel");
            }
        }
    }
}
