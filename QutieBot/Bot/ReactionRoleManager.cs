using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using QutieDAL.DAL;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace QutieBot.Bot
{
    /// <summary>
    /// Manages reaction role functionality for the bot
    /// </summary>
    public class ReactionRoleManager
    {
        private readonly ReactionRoleManagerDAL _databaseManager;
        private readonly ILogger<ReactionRoleManager> _logger;

        /// <summary>
        /// Initializes a new instance of the ReactionRoleManager class
        /// </summary>
        /// <param name="databaseManager">The database manager for reaction roles</param>
        /// <param name="logger">The logger instance</param>
        public ReactionRoleManager(
            ReactionRoleManagerDAL databaseManager,
            ILogger<ReactionRoleManager> logger)
        {
            _databaseManager = databaseManager ?? throw new ArgumentNullException(nameof(databaseManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Handles message reaction added events to assign roles based on configurations
        /// </summary>
        /// <param name="sender">The Discord client that raised the event</param>
        /// <param name="e">Event arguments containing reaction information</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task OnMessageReactionAdded(DiscordClient sender, MessageReactionAddedEventArgs e)
        {
            try
            {
                _logger.LogDebug($"Reaction added by user {e.User.Id} to message {e.Message.Id} in channel {e.Channel.Id} with emoji {e.Emoji.Name}");

                var configurations = await _databaseManager.GetAllReactionRoleConfigsAsync();
                if (configurations == null || configurations.Count == 0)
                {
                    _logger.LogDebug("No reaction role configurations found");
                    return;
                }

                _logger.LogDebug($"Processing {configurations.Count} reaction role configurations");

                // Get the guild member who reacted
                if (!e.Guild.Members.TryGetValue(e.User.Id, out var reactor))
                {
                    _logger.LogWarning($"Could not find reactor {e.User.Id} in guild members");
                    return;
                }

                foreach (var config in configurations)
                {
                    // Skip if the configuration is channel-specific and this isn't the right channel
                    if (config.OnlyOneChannelId.HasValue && (long)e.Channel.Id != config.OnlyOneChannelId.Value)
                    {
                        continue;
                    }

                    // Check if the reactor has the required moderator role
                    if (!reactor.Roles.Any(r => (long)r.Id == config.ModeratorRoleId))
                    {
                        continue;
                    }

                    // Check if the emoji matches the configuration
                    if (e.Emoji.Name != config.Emoji)
                    {
                        continue;
                    }

                    _logger.LogInformation($"Valid reaction configuration found for emoji {config.Emoji}");

                    // Get the message that was reacted to
                    var message = await e.Channel.GetMessageAsync(e.Message.Id);

                    // Try to get the message author as a guild member
                    if (!e.Guild.Members.TryGetValue(message.Author.Id, out var authorMember))
                    {
                        _logger.LogWarning($"Could not find reactor {message.Author.Id} in guild members");
                        continue;
                    }

                    // Grant the verification role to the message author
                    try
                    {
                        var roleToGrant = await e.Guild.GetRoleAsync((ulong)config.VerificationRoleId);
                        await authorMember.GrantRoleAsync(roleToGrant);
                        _logger.LogInformation($"Granted role {roleToGrant.Name} ({roleToGrant.Id}) to user {authorMember.Username} ({authorMember.Id})");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error granting role {config.VerificationRoleId} to user {authorMember.Id}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in OnMessageReactionAdded");
            }
        }
    }

    /// <summary>
    /// Represents a reaction role configuration
    /// </summary>
    public class ReactionRoleConfiguration
    {
        /// <summary>
        /// The emoji that triggers the role assignment
        /// </summary>
        public string Emoji { get; set; }

        /// <summary>
        /// The ID of the role that can assign roles through reactions
        /// </summary>
        public ulong ModeratorRoleId { get; set; }

        /// <summary>
        /// The ID of the role to be assigned
        /// </summary>
        public ulong VerifiedRoleId { get; set; }

        /// <summary>
        /// Optional channel ID to restrict the configuration to a specific channel
        /// </summary>
        public ulong? OnlyOneChannelId { get; set; }
    }
}