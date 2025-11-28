using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using QutieDAL.DAL;
using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QutieBot.Bot
{
    /// <summary>
    /// Manages interview rooms for user applications
    /// </summary>
    public class InterviewRoom
    {
        // Configuration
        private readonly ulong _categoryId;
        private readonly ulong _adminRoleId;

        // Dependencies
        private readonly DiscordClient _client;
        private StateManager _stateManager;
        private readonly ILogger<InterviewRoom> _logger;
        private readonly DiscordInfoSaverDAL _discordInfoSaverDAL;
        private InterviewFollowUpService _followUpService;

        // Active interview tracking
        private readonly Dictionary<ulong, InterviewData> _activeInterviews = new Dictionary<ulong, InterviewData>();

        /// <summary>
        /// Constructs a new InterviewRoom manager
        /// </summary>
        public InterviewRoom(
            DiscordClient client,
            ILogger<InterviewRoom> logger,
            DiscordInfoSaverDAL discordInfoSaverDAL,
            InterviewFollowUpService followUpService,
            StateManager stateManager,
            ulong categoryId = 1308761605105909790,
            ulong adminRoleId = 1152617541190041600)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _categoryId = categoryId;
            _adminRoleId = adminRoleId;
            _discordInfoSaverDAL = discordInfoSaverDAL;
            _followUpService = followUpService;
            _stateManager = stateManager;
            followUpService.SetInterviewRoom(this);

            // Load persisted state
            var savedInterviews = _stateManager.GetActiveInterviews();
            foreach (var (userId, interview) in savedInterviews)
            {
                _activeInterviews[userId] = new InterviewData
                {
                    UserId = interview.UserId,
                    ChannelId = interview.ChannelId,
                    SubmissionId = interview.SubmissionId,
                    CreatedAt = interview.CreatedAt
                };
            }

            _logger.LogInformation($"InterviewRoom initialized with category {_categoryId} and admin role {_adminRoleId}");
        }

        /// <summary>
        /// Validates that persisted interview channels still exist on Discord
        /// </summary>
        public async Task ValidateActiveInterviewsAsync(DiscordClient client)
        {
            try
            {
                var toRemove = new List<ulong>();

                foreach (var (userId, interview) in _activeInterviews)
                {
                    try
                    {
                        var channel = await client.GetChannelAsync(interview.ChannelId);
                        if (channel == null)
                        {
                            toRemove.Add(userId);
                        }
                    }
                    catch
                    {
                        // Channel doesn't exist anymore
                        toRemove.Add(userId);
                    }
                }

                foreach (var userId in toRemove)
                {
                    _activeInterviews.Remove(userId);
                    _stateManager.RemoveActiveInterview(userId);
                    _logger.LogInformation($"Removed stale interview for user {userId} from tracking");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating active interviews");
            }
        }

        /// <summary>
        /// Creates a new interview room for a user
        /// </summary>
        /// <param name="userId">Discord user ID</param>
        /// <param name="submissionId">Application submission ID</param>
        /// <returns>The created channel ID or null if failed</returns>
        public async Task<ulong?> CreateInterviewRoomAsync(ulong userId, long submissionId)
        {
            try
            {
                _logger.LogInformation($"Creating interview room for user {userId} with submission {submissionId}");

                // Check if user already has an interview room
                if (_activeInterviews.ContainsKey(userId))
                {
                    _logger.LogWarning($"User {userId} already has an active interview room: {_activeInterviews[userId].ChannelId}");
                    return _activeInterviews[userId].ChannelId;
                }

                // Get necessary discord objects
                var guild = _client.Guilds.Values.FirstOrDefault();
                if (guild == null)
                {
                    _logger.LogError("No guilds available - bot must be in at least one server");
                    return null;
                }

                // Get user
                DiscordMember user;
                try
                {
                    user = await guild.GetMemberAsync(userId);
                    if (user == null)
                    {
                        _logger.LogError($"Could not find user {userId} in guild {guild.Id}");
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error retrieving user {userId} from guild {guild.Id}");
                    return null;
                }

                // Get admin role
                DiscordRole adminRole;
                try
                {
                    adminRole = await guild.GetRoleAsync(_adminRoleId);
                    if (adminRole == null)
                    {
                        _logger.LogError($"Admin role {_adminRoleId} not found in guild {guild.Id}");
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error retrieving admin role {_adminRoleId} from guild {guild.Id}");
                    return null;
                }

                // Get category channel
                if (!guild.Channels.TryGetValue(_categoryId, out var categoryChannel) || categoryChannel.Type != DiscordChannelType.Category)
                {
                    _logger.LogError($"Category channel {_categoryId} not found or is not a category");
                    return null;
                }

                // Create channel permissions
                var channelName = $"{user.DisplayName}-interview";
                var permissionOverwrites = new List<DiscordOverwriteBuilder>
                {
                    // Deny access to everyone by default
                    new DiscordOverwriteBuilder(guild.EveryoneRole)
                        .Deny(DiscordPermissions.AccessChannels),

                    // Allow the applicant access
                    new DiscordOverwriteBuilder(user)
                        .Allow(DiscordPermissions.AccessChannels |
                               DiscordPermissions.SendMessages |
                               DiscordPermissions.ReadMessageHistory |
                               DiscordPermissions.AddReactions |
                               DiscordPermissions.UseExternalEmojis |
                               DiscordPermissions.UseExternalStickers |
                               DiscordPermissions.EmbedLinks |
                               DiscordPermissions.AttachFiles |
                               DiscordPermissions.UseApplicationCommands |
                               DiscordPermissions.SendMessagesInThreads |
                               DiscordPermissions.CreatePublicThreads |
                               DiscordPermissions.CreatePrivateThreads),

                    // Give admins full access
                    new DiscordOverwriteBuilder(adminRole)
                        .Allow(DiscordPermissions.AccessChannels |
                               DiscordPermissions.ManageChannels |
                               DiscordPermissions.SendMessages |
                               DiscordPermissions.ReadMessageHistory |
                               DiscordPermissions.AddReactions |
                               DiscordPermissions.UseExternalEmojis |
                               DiscordPermissions.UseExternalStickers |
                               DiscordPermissions.EmbedLinks |
                               DiscordPermissions.AttachFiles |
                               DiscordPermissions.UseApplicationCommands |
                               DiscordPermissions.SendMessagesInThreads |
                               DiscordPermissions.CreatePublicThreads |
                               DiscordPermissions.CreatePrivateThreads |
                               DiscordPermissions.ManageThreads |
                               DiscordPermissions.ManageMessages |
                               DiscordPermissions.MentionEveryone |
                               DiscordPermissions.ManageRoles)
                };

                // Create the channel
                DiscordChannel interviewChannel;
                try
                {
                    interviewChannel = await guild.CreateTextChannelAsync(
                        channelName,
                        categoryChannel,
                        "Interview room created for application",
                        permissionOverwrites);

                    _logger.LogInformation($"Created interview channel {interviewChannel.Id} for user {userId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error creating interview channel for user {userId}");
                    return null;
                }

                // Set the topic with a link to the submission
                var topicUrl = $"https://quintessence-eu.com/menu/submissions/{submissionId}";
                try
                {
                    await interviewChannel.ModifyAsync(channel =>
                        channel.Topic = $"[View Application](<{topicUrl}>)");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Error setting channel topic for {interviewChannel.Id}");
                    // Continue anyway, this is not critical
                }

                // Track this interview
                var interviewData = new InterviewData
                {
                    UserId = userId,
                    ChannelId = interviewChannel.Id,
                    SubmissionId = submissionId,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _activeInterviews[userId] = interviewData;

                _stateManager.UpdateActiveInterview(userId, new InterviewDataState
                {
                    ChannelId = interviewChannel.Id,
                    SubmissionId = submissionId,
                    CreatedAt = interviewData.CreatedAt
                });

                // Send welcome message
                await SendWelcomeMessageAsync(interviewChannel, user, adminRole);

                return interviewChannel.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected error creating interview room for user {userId}");
                return null;
            }
        }

        /// <summary>
        /// Sends the welcome message with instructions and a close button
        /// </summary>
        private async Task SendWelcomeMessageAsync(DiscordChannel channel, DiscordMember user, DiscordRole adminRole)
        {
            try
            {
                var closeButton = new DiscordButtonComponent(
                    DiscordButtonStyle.Danger,
                    $"close_channel_{channel.Id}",
                    "Close Interview");

                var welcomeEmbed = new DiscordEmbedBuilder()
                    .WithTitle("🎉 Welcome to Quintessence!")
                    .WithDescription($"Thank you for your interest in joining our community, {user.Mention}!")
                    .WithColor(DiscordColor.Blurple)
                    .AddField("Next Steps",
                        "We hope you've taken the time to review and meet our [requirements](https://quintessence-eu.com/#requirements). " +
                        "If you have any questions about our community or your application, please feel free to ask here. " +
                        $"One of our {adminRole.Mention} team members will review your submission and get back to you shortly.")
                    .WithFooter("Quintessence Application System")
                    .WithTimestamp(DateTimeOffset.UtcNow);

                var contentMessage = $"{adminRole.Mention} {user.Mention}";
                var messageBuilder = new DiscordMessageBuilder()
                    .WithContent(contentMessage)
                    .AddEmbed(welcomeEmbed)
                    .AddComponents(closeButton)
                    .WithAllowedMentions(Mentions.All);

                await channel.SendMessageAsync(messageBuilder);
                _logger.LogInformation($"Sent welcome message to channel {channel.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send welcome message to channel {channel.Id}");
            }
        }

        /// <summary>
        /// Cleans up interview data when closed due to timeout
        /// </summary>
        public async Task CleanupInterviewOnTimeout(ulong userId, ulong channelId)
        {
            try
            {
                if (_activeInterviews.TryGetValue(userId, out var interviewData))
                {
                    await _discordInfoSaverDAL.UpdateDatabaseForUserLeft(userId, interviewData.SubmissionId);
                    _activeInterviews.Remove(userId);
                    _stateManager.RemoveActiveInterview(userId);
                    _logger.LogInformation($"Cleaned up interview data for user {userId} after timeout");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cleaning up interview for user {userId}");
            }
        }

        /// <summary>
        /// Checks if a user who left the server has an interview room
        /// </summary>
        public async Task HandleUserLeftAsync(DiscordClient client, GuildMemberRemovedEventArgs e)
        {
            try
            {
                var userId = e.Member.Id;
                _logger.LogInformation($"Checking if user {userId} who left has an interview room");

                if (_activeInterviews.TryGetValue(userId, out var interviewData))
                {
                    var channelId = interviewData.ChannelId;
                    var submissionId = interviewData.SubmissionId;
                    _logger.LogInformation($"User {userId} left with active interview room {channelId}");

                    if (_followUpService != null)
                    {
                        await _followUpService.ManualCancelTimerAsync(channelId);
                    }

                    await _discordInfoSaverDAL.UpdateDatabaseForUserLeft(userId, submissionId);

                    // Get the channel
                    DiscordChannel channel;
                    try
                    {
                        channel = await client.GetChannelAsync(channelId);
                        if (channel == null)
                        {
                            _logger.LogWarning($"Could not find channel {channelId} for user {userId} who left");
                            _activeInterviews.Remove(userId);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error retrieving channel {channelId} for user {userId} who left");
                        _activeInterviews.Remove(userId);
                        return;
                    }


                    // Create a notification embed
                    var notificationEmbed = new DiscordEmbedBuilder()
                        .WithTitle("⚠️ User Left Server")
                        .WithDescription($"User <@{userId}> has left the server.")
                        .WithColor(DiscordColor.Red)
                        .AddField("Options", "You can either keep this channel for record-keeping or delete it.")
                        .WithTimestamp(DateTimeOffset.UtcNow);

                    // Create buttons
                    var deleteButton = new DiscordButtonComponent(
                        DiscordButtonStyle.Danger,
                        $"delete_channel_{channel.Id}",
                        "Delete Channel");

                    var archiveButton = new DiscordButtonComponent(
                        DiscordButtonStyle.Secondary,
                        $"archive_channel_{channel.Id}",
                        "Archive Channel");

                    var messageBuilder = new DiscordMessageBuilder()
                        .AddEmbed(notificationEmbed)
                        .AddComponents(archiveButton, deleteButton);

                    await channel.SendMessageAsync(messageBuilder);
                    _activeInterviews.Remove(userId);
                    _stateManager.RemoveActiveInterview(userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling user {e.Member.Id} leaving the server");
            }
        }

        /// <summary>
        /// Handles button press events for interview rooms
        /// </summary>
        public async Task HandleButtonPressAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e)
        {
            // Get the user who pressed the button
            var buttonUserId = e.User.Id;

            try
            {
                _logger.LogInformation($"Button pressed: {e.Id} by user {buttonUserId}");

                // Handle close channel button
                if (e.Id.StartsWith("close_channel_"))
                {
                    await HandleCloseChannelButtonAsync(client, e);
                }
                // Handle delete channel button
                else if (e.Id.StartsWith("delete_channel_"))
                {
                    await HandleDeleteChannelButtonAsync(client, e);
                }
                // Handle archive channel button
                else if (e.Id.StartsWith("archive_channel_"))
                {
                    await HandleArchiveChannelButtonAsync(client, e);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling button press {e.Id} by user {buttonUserId}");

                // Try to respond with an error message
                try
                {
                    await e.Interaction.CreateResponseAsync(
                        DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder()
                            .WithContent("❌ An error occurred. Please try again or contact an administrator.")
                            .AsEphemeral(true));
                }
                catch
                {
                    // Ignore any errors in the error handler
                }
            }
        }

        /// <summary>
        /// Handles the "Close Interview" button press
        /// </summary>
        private async Task HandleCloseChannelButtonAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e)
        {
            // Extract channel ID from button ID
            var channelId = ulong.Parse(e.Id.Replace("close_channel_", string.Empty));
            var channel = e.Channel;
            var guild = channel.Guild;
            var buttonUser = await guild.GetMemberAsync(e.User.Id);

            _logger.LogInformation($"Handling close button for channel {channelId} pressed by {buttonUser.Id}");

            // Check if user has permission (admin role)
            if (!buttonUser.Roles.Any(r => r.Id == _adminRoleId))
            {
                _logger.LogWarning($"User {buttonUser.Id} lacks permission to close channel {channelId}");
                await e.Interaction.CreateResponseAsync(
                    DiscordInteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder()
                        .WithContent("❌ You don't have permission to close this interview. Only admins can do this.")
                        .AsEphemeral(true));
                return;
            }

            if (_followUpService != null)
            {
                await _followUpService.ManualCancelTimerAsync(channelId);
            }

            // Find the user who this interview belongs to
            var userOverwrite = channel.PermissionOverwrites
                .FirstOrDefault(o => o.Type == DiscordOverwriteType.Member &&
                                    o.Allowed.HasPermission(DiscordPermissions.AccessChannels));

            if (userOverwrite != null)
            {
                var userId = userOverwrite.Id;
                var user = await guild.GetMemberAsync(userId);

                // Change permissions to deny access
                await channel.AddOverwriteAsync(
                    user,
                    deny: DiscordPermissions.AccessChannels,
                    allow: DiscordPermissions.None);

                _logger.LogInformation($"Removed access for user {userId} to channel {channelId}");
            }

            // Acknowledge the interaction
            await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

            // Remove the button from the original message
            var originalMessage = await e.Channel.GetMessageAsync(e.Message.Id);
            if (originalMessage != null)
            {
                await originalMessage.ModifyAsync(msg => msg.ClearComponents());
            }

            // Create a follow-up message with options
            var closedEmbed = new DiscordEmbedBuilder()
                .WithTitle("🔒 Interview Closed")
                .WithDescription("This interview has been closed. The applicant can no longer access this channel.")
                .WithColor(DiscordColor.Gold)
                .AddField("Options", "You can either keep this channel for record-keeping, archive it, or delete it.")
                .WithTimestamp(DateTimeOffset.UtcNow);

            var deleteButton = new DiscordButtonComponent(
                DiscordButtonStyle.Danger,
                $"delete_channel_{channel.Id}",
                "Delete Channel");

            var archiveButton = new DiscordButtonComponent(
                DiscordButtonStyle.Secondary,
                $"archive_channel_{channel.Id}",
                "Archive Channel");

            var messageBuilder = new DiscordMessageBuilder()
                .AddEmbed(closedEmbed)
                .AddComponents(archiveButton, deleteButton);

            // Remove from active interviews if present
            foreach (var entry in _activeInterviews.Where(kvp => kvp.Value.ChannelId == channelId).ToList())
            {
                _activeInterviews.Remove(entry.Key);
                _stateManager.RemoveActiveInterview(entry.Key);
                _logger.LogInformation($"Removed user {entry.Key} from active interviews");
            }

            await channel.SendMessageAsync(messageBuilder);
        }

        /// <summary>
        /// Handles the "Delete Channel" button press
        /// </summary>
        private async Task HandleDeleteChannelButtonAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e)
        {
            // Extract channel ID from button ID
            var channelId = ulong.Parse(e.Id.Replace("delete_channel_", string.Empty));
            var channel = e.Channel;
            var guild = channel.Guild;
            var buttonUser = await guild.GetMemberAsync(e.User.Id);

            _logger.LogInformation($"Handling delete button for channel {channelId} pressed by {buttonUser.Id}");

            // Check if user has permission (admin role)
            if (!buttonUser.Roles.Any(r => r.Id == _adminRoleId))
            {
                _logger.LogWarning($"User {buttonUser.Id} lacks permission to delete channel {channelId}");
                await e.Interaction.CreateResponseAsync(
                    DiscordInteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder()
                        .WithContent("❌ You don't have permission to delete this channel. Only admins can do this.")
                        .AsEphemeral(true));
                return;
            }

            if (_followUpService != null)
            {
                await _followUpService.ManualCancelTimerAsync(channelId);
            }

            // Acknowledge the interaction
            await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

            // Delete the channel
            await channel.DeleteAsync("Interview channel deleted by admin");
            _logger.LogInformation($"Channel {channelId} deleted by admin {buttonUser.Id}");

            // Remove from active interviews if present
            foreach (var entry in _activeInterviews.Where(kvp => kvp.Value.ChannelId == channelId).ToList())
            {
                _activeInterviews.Remove(entry.Key);
                _stateManager.RemoveActiveInterview(entry.Key);
            }
        }

        /// <summary>
        /// Handles the "Archive Channel" button press
        /// </summary>
        private async Task HandleArchiveChannelButtonAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e)
        {
            // Extract channel ID from button ID
            var channelId = ulong.Parse(e.Id.Replace("archive_channel_", string.Empty));
            var channel = e.Channel;
            var guild = channel.Guild;
            var buttonUser = await guild.GetMemberAsync(e.User.Id);

            _logger.LogInformation($"Handling archive button for channel {channelId} pressed by {buttonUser.Id}");

            // Check if user has permission (admin role)
            if (!buttonUser.Roles.Any(r => r.Id == _adminRoleId))
            {
                _logger.LogWarning($"User {buttonUser.Id} lacks permission to archive channel {channelId}");
                await e.Interaction.CreateResponseAsync(
                    DiscordInteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder()
                        .WithContent("❌ You don't have permission to archive this channel. Only admins can do this.")
                        .AsEphemeral(true));
                return;
            }

            if (_followUpService != null)
            {
                await _followUpService.ManualCancelTimerAsync(channelId);
            }

            // Acknowledge the interaction
            await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.DeferredMessageUpdate);

            // Rename the channel to indicate it's archived
            var currentName = channel.Name;
            var newName = currentName.StartsWith("archived-") ? currentName : $"archived-{currentName}";

            try
            {
                await channel.ModifyAsync(c => {
                    c.Name = newName;
                    c.Position = channel.Position; // Keep the same position
                });

                _logger.LogInformation($"Channel {channelId} archived and renamed to {newName}");

                // Send confirmation message
                var archiveEmbed = new DiscordEmbedBuilder()
                    .WithTitle("📁 Channel Archived")
                    .WithDescription("This channel has been archived for record-keeping.")
                    .WithColor(DiscordColor.Blurple)
                    .WithTimestamp(DateTimeOffset.UtcNow);

                await channel.SendMessageAsync(archiveEmbed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error archiving channel {channelId}");

                await channel.SendMessageAsync(new DiscordEmbedBuilder()
                    .WithTitle("❌ Archive Failed")
                    .WithDescription("An error occurred while trying to archive this channel.")
                    .WithColor(DiscordColor.Red));
            }

            // Remove from active interviews
            foreach (var entry in _activeInterviews.Where(kvp => kvp.Value.ChannelId == channelId).ToList())
            {
                _activeInterviews.Remove(entry.Key);
                _stateManager.RemoveActiveInterview(entry.Key);
            }
        }

        /// <summary>
        /// Gets all active interview channels
        /// </summary>
        public IReadOnlyDictionary<ulong, InterviewData> GetActiveInterviews()
        {
            return _activeInterviews;
        }

        /// <summary>
        /// Gets the interview channel for a specific user, if it exists
        /// </summary>
        public bool TryGetInterviewChannel(ulong userId, out ulong channelId)
        {
            if (_activeInterviews.TryGetValue(userId, out var data))
            {
                channelId = data.ChannelId;
                return true;
            }

            channelId = 0;
            return false;
        }
    }

    /// <summary>
    /// Stores data about an active interview
    /// </summary>
    public class InterviewData
    {
        /// <summary>
        /// Discord user ID of the interviewee
        /// </summary>
        public ulong UserId { get; set; }

        /// <summary>
        /// Discord channel ID for the interview
        /// </summary>
        public ulong ChannelId { get; set; }

        /// <summary>
        /// Application submission ID
        /// </summary>
        public long SubmissionId { get; set; }

        /// <summary>
        /// When the interview room was created
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; }
    }
}