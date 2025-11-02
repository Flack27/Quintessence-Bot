using DSharpPlus.EventArgs;
using DSharpPlus;
using QutieBot.Bot;
using System;
using System.Threading.Tasks;
using QutieBot.Bot.GoogleSheets;
using Microsoft.Extensions.Logging;
using DSharpPlus.Entities;

namespace QutieBot
{
    /// <summary>
    /// Handles Discord events and routes them to the appropriate bot services
    /// </summary>
    public class EventHandlers :
        IEventHandler<ComponentInteractionCreatedEventArgs>,
        IEventHandler<GuildAvailableEventArgs>,
        IEventHandler<GuildRoleCreatedEventArgs>,
        IEventHandler<GuildRoleUpdatedEventArgs>,
        IEventHandler<GuildRoleDeletedEventArgs>,
        IEventHandler<GuildMemberAddedEventArgs>,
        IEventHandler<GuildMemberUpdatedEventArgs>,
        IEventHandler<GuildMemberRemovedEventArgs>,
        IEventHandler<ChannelCreatedEventArgs>,
        IEventHandler<ChannelUpdatedEventArgs>,
        IEventHandler<ChannelDeletedEventArgs>,
        IEventHandler<VoiceStateUpdatedEventArgs>,
        IEventHandler<MessageCreatedEventArgs>,
        IEventHandler<MessageReactionAddedEventArgs>,
        IEventHandler<MessageReactionRemovedEventArgs>
    {
        private readonly DiscordInfoSaver _discordInfoSaver;
        private readonly JoinToCreateManager _joinToCreateChannelBot;
        private readonly UserMessageXPCounter _userMessageXPCounter;
        private readonly UserVoiceXPCounter _userVoiceXPCounter;
        private readonly ReactionRoleManager _reactionRoleBot;
        private readonly ReactionRoleHandler _reactionRoleHandler;
        private readonly InterviewRoom _interviewRoom;
        private readonly GoogleSheetsFacade _googleSheets;
        private readonly WelcomeLeaveMessenger _welcomeLeaveMessenger;
        private readonly AutoRoleManager _autoRoleManager;
        private readonly ILogger<EventHandlers> _logger;

        /// <summary>
        /// Initializes a new instance of the EventHandlers class
        /// </summary>
        public EventHandlers(
            DiscordInfoSaver discordInfoSaver,
            JoinToCreateManager joinToCreateChannelBot,
            UserMessageXPCounter userMessageXPCounter,
            UserVoiceXPCounter userVoiceXPCounter,
            ReactionRoleManager reactionRoleBot,
            InterviewRoom interviewRoom,
            GoogleSheetsFacade googleSheets,
            ReactionRoleHandler reactionRoleHandler,
            WelcomeLeaveMessenger welcomeLeaveMessenger,
            AutoRoleManager autoRoleManager,
            ILogger<EventHandlers> logger)
        {
            _discordInfoSaver = discordInfoSaver ?? throw new ArgumentNullException(nameof(discordInfoSaver));
            _joinToCreateChannelBot = joinToCreateChannelBot ?? throw new ArgumentNullException(nameof(joinToCreateChannelBot));
            _userMessageXPCounter = userMessageXPCounter ?? throw new ArgumentNullException(nameof(userMessageXPCounter));
            _userVoiceXPCounter = userVoiceXPCounter ?? throw new ArgumentNullException(nameof(userVoiceXPCounter));
            _reactionRoleBot = reactionRoleBot ?? throw new ArgumentNullException(nameof(reactionRoleBot));
            _interviewRoom = interviewRoom ?? throw new ArgumentNullException(nameof(interviewRoom));
            _googleSheets = googleSheets ?? throw new ArgumentNullException(nameof(googleSheets));
            _reactionRoleHandler = reactionRoleHandler ?? throw new ArgumentNullException(nameof(reactionRoleHandler));
            _welcomeLeaveMessenger = welcomeLeaveMessenger ?? throw new ArgumentNullException(nameof(welcomeLeaveMessenger));
            _autoRoleManager = autoRoleManager ?? throw new ArgumentNullException(nameof(autoRoleManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Handles role creation events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, GuildRoleCreatedEventArgs e)
        {
            try
            {
                _logger.LogDebug($"Role created: {e.Role.Name} ({e.Role.Id}) in guild {e.Guild.Name}");
                await _discordInfoSaver.Client_GuildRoleCreated(client, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling role creation for role {e.Role.Name} ({e.Role.Id})");
            }
        }

        /// <summary>
        /// Handles role update events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, GuildRoleUpdatedEventArgs e)
        {
            try
            {
                _logger.LogDebug($"Role updated: {e.RoleAfter.Name} ({e.RoleAfter.Id}) in guild {e.Guild.Name}");
                await _discordInfoSaver.Client_GuildRoleUpdated(client, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling role update for role {e.RoleAfter.Name} ({e.RoleAfter.Id})");
            }
        }

        /// <summary>
        /// Handles role deletion events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, GuildRoleDeletedEventArgs e)
        {
            try
            {
                _logger.LogDebug($"Role deleted: {e.Role.Name} ({e.Role.Id}) in guild {e.Guild.Name}");
                await _discordInfoSaver.Client_GuildRoleDeleted(client, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling role deletion for role {e.Role.Name} ({e.Role.Id})");
            }
        }

        /// <summary>
        /// Handles member join events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, GuildMemberAddedEventArgs e)
        {
            try
            {
                _logger.LogInformation($"Member joined: {e.Member.Username} ({e.Member.Id}) in guild {e.Guild.Name}");

                var tasks = new Task[]
                {
                    _discordInfoSaver.Client_GuildMemberAdded(client, e),
                    _welcomeLeaveMessenger.SendWelcomeMessageAsync(client, e),
                    _autoRoleManager.AssignAutoRolesAsync(client, e)
                };

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling member join for user {e.Member.Username} ({e.Member.Id})");
            }
        }

        /// <summary>
        /// Handles member update events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, GuildMemberUpdatedEventArgs e)
        {
            try
            {
                _logger.LogDebug($"Member updated: {e.Member.Username} ({e.Member.Id}) in guild {e.Guild.Name}");

                var tasks = new Task[]
                {
                    _discordInfoSaver.Client_GuildMemberUpdated(client, e),
                    _googleSheets.ProcessRoleChangeAsync(e)
                };

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling member update for user {e.Member.Username} ({e.Member.Id})");
            }
        }

        /// <summary>
        /// Handles member leave events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, GuildMemberRemovedEventArgs e)
        {
            try
            {
                _logger.LogInformation($"Member left: {e.Member.Username} ({e.Member.Id}) from guild {e.Guild.Name}");

                var tasks = new Task[]
                {
                    _discordInfoSaver.Client_GuildMemberRemoved(client, e),
                    _interviewRoom.HandleUserLeftAsync(client, e),
                    _welcomeLeaveMessenger.SendLeaveMessageAsync(client, e)
                };

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling member leave for user {e.Member.Username} ({e.Member.Id})");
            }
        }

        /// <summary>
        /// Handles channel creation events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, ChannelCreatedEventArgs e)
        {
            try
            {
                _logger.LogDebug($"Channel created: {e.Channel.Name} ({e.Channel.Id})");
                await _discordInfoSaver.Client_ChannelCreated(client, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling channel creation for channel {e.Channel.Name} ({e.Channel.Id})");
            }
        }

        /// <summary>
        /// Handles channel update events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, ChannelUpdatedEventArgs e)
        {
            try
            {
                _logger.LogDebug($"Channel updated: {e.ChannelAfter.Name} ({e.ChannelAfter.Id})");
                await _discordInfoSaver.Client_ChannelUpdated(client, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling channel update for channel {e.ChannelAfter.Name} ({e.ChannelAfter.Id})");
            }
        }

        /// <summary>
        /// Handles channel deletion events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, ChannelDeletedEventArgs e)
        {
            try
            {
                _logger.LogDebug($"Channel deleted: {e.Channel.Name} ({e.Channel.Id})");
                await _discordInfoSaver.Client_ChannelDeleted(client, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling channel deletion for channel {e.Channel.Name} ({e.Channel.Id})");
            }
        }

        /// <summary>
        /// Handles voice state update events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, VoiceStateUpdatedEventArgs e)
        {
            try
            {
                // Only log if there's actually a change in voice state (joined/left/moved)
                if (e.Before?.Channel?.Id != e.After?.Channel?.Id)
                {
                    string action = GetVoiceAction(e.Before?.Channel, e.After?.Channel);
                    _logger.LogDebug($"Voice state changed: {e.User.Username} ({e.User.Id}) {action}");
                }

                var tasks = new Task[]
                {
                    _userVoiceXPCounter.VoiceStateUpdated(client, e),
                    _joinToCreateChannelBot.HandleVoiceStateUpdatedAsync(client, e)
                };

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling voice state update for user {e.User.Username} ({e.User.Id})");
            }
        }

        /// <summary>
        /// Handles message creation events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, MessageCreatedEventArgs e)
        {
            try
            {
                // Only log non-bot messages to prevent log spam
                if (!e.Author.IsBot)
                {
                    string channelName = e.Channel.Name ?? "DM";
                    _logger.LogDebug($"Message from {e.Author.Username} ({e.Author.Id}) in {channelName}");
                }

                await _userMessageXPCounter.OnMessageReceived(client, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling message from user {e.Author.Username} ({e.Author.Id})");
            }
        }

        /// <summary>
        /// Handles reaction add events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, MessageReactionAddedEventArgs e)
        {
            try
            {
                _logger.LogDebug($"Reaction added: {e.Emoji.Name} by {e.User.Username} ({e.User.Id}) to message {e.Message.Id}");

                var tasks = new Task[]
                {
                    _reactionRoleBot.OnMessageReactionAdded(client, e),
                    _reactionRoleHandler.OnReactionAdded(client, e)
                };

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling reaction add from user {e.User.Username} ({e.User.Id})");
            }
        }

        /// <summary>
        /// Handles reaction remove events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, MessageReactionRemovedEventArgs e)
        {
            try
            {
                _logger.LogDebug($"Reaction removed: {e.Emoji.Name} by {e.User.Username} ({e.User.Id}) from message {e.Message.Id}");
                await _reactionRoleHandler.OnReactionRemoved(client, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling reaction remove from user {e.User.Username} ({e.User.Id})");
            }
        }

        /// <summary>
        /// Handles component interaction events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e)
        {
            try
            {
                _logger.LogDebug($"Component interaction: {e.Id} by {e.User.Username} ({e.User.Id})");
                await _interviewRoom.HandleButtonPressAsync(client, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling component interaction from user {e.User.Username} ({e.User.Id})");
            }
        }

        /// <summary>
        /// Handles guild available events
        /// </summary>
        public async Task HandleEventAsync(DiscordClient client, GuildAvailableEventArgs e)
        {
            try
            {
                _logger.LogInformation($"Guild available: {e.Guild.Name} ({e.Guild.Id})");
                await _discordInfoSaver.Client_GuildAvailable(client, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling guild available for guild {e.Guild.Name} ({e.Guild.Id})");
            }
        }

        /// <summary>
        /// Gets a human-readable description of a voice state change
        /// </summary>
        private string GetVoiceAction(DiscordChannel before, DiscordChannel after)
        {
            if (before == null && after != null)
                return $"joined {after.Name}";
            else if (before != null && after == null)
                return $"left {before.Name}";
            else if (before != null && after != null && before.Id != after.Id)
                return $"moved from {before.Name} to {after.Name}";
            else
                return "updated voice state";
        }
    }
}