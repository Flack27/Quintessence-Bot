using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.Extensions.Logging;
using QutieDAL.DAL;
using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace QutieBot.Bot
{
    /// <summary>
    /// Manages the creation and deletion of temporary voice channels
    /// when users join designated "join-to-create" channels
    /// </summary>
    public class JoinToCreateManager
    {
        // Configuration and state
        private List<ulong> _targetChannelIds = new List<ulong>();
        private readonly StateManager _stateManager;
        private readonly Dictionary<ulong, List<ulong>> _createdChannels = new Dictionary<ulong, List<ulong>>();

        // Dependencies
        private readonly JoinToCreateManagerDAL _dal;
        private readonly ILogger<JoinToCreateManager> _logger;

        /// <summary>
        /// Initializes a new instance of the JoinToCreateManager class
        /// </summary>
        /// <param name="dal">The data access layer for join-to-create channels</param>
        /// <param name="logger">The logger instance</param>
        public JoinToCreateManager(
            JoinToCreateManagerDAL dal,
            StateManager stateManager,
            ILogger<JoinToCreateManager> logger)
        {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _stateManager = stateManager;

            // Load persisted state
            _createdChannels = _stateManager.GetJtcCreatedChannels();

            _logger.LogInformation("JoinToCreateManager initialized");

            _ = InitializeAsync();
        }

        /// <summary>
        /// Initializes the manager by loading join-to-create channels from the database
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                _logger.LogInformation("Initializing JoinToCreateManager");

                var channels = await GetChannels();
                _targetChannelIds = channels.Select(c => (ulong)c.ChannelId).ToList();

                _logger.LogInformation($"Loaded {_targetChannelIds.Count} join-to-create channels");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing JoinToCreateManager");
                _targetChannelIds = new List<ulong>();
            }
        }

        /// <summary>
        /// Stores a new join-to-create channel in the database
        /// </summary>
        /// <param name="channelId">The channel ID</param>
        /// <param name="channelName">The channel name</param>
        /// <param name="category">The category name</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> StoreJoinToCreateChannel(long channelId, string channelName, string category)
        {
            try
            {
                _logger.LogInformation($"Storing join-to-create channel {channelId} ({channelName}) in category '{category}'");

                var channel = new JoinToCreateChannel
                {
                    ChannelId = channelId,
                    ChannelName = channelName,
                    Category = category
                };

                bool success = await _dal.StoreJoinToCreateChannelAsync(channel);

                if (success)
                {
                    _logger.LogInformation($"Successfully stored join-to-create channel {channelId}");
                }
                else
                {
                    _logger.LogWarning($"Failed to store join-to-create channel {channelId}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error storing join-to-create channel {channelId}");
                return false;
            }
        }

        /// <summary>
        /// Handles voice state update events to create or delete temporary channels
        /// </summary>
        /// <param name="sender">The Discord client</param>
        /// <param name="e">The voice state update event arguments</param>
        public async Task HandleVoiceStateUpdatedAsync(DiscordClient sender, VoiceStateUpdatedEventArgs e)
        {
            try
            {
                // Validate basic requirements
                if (e.User == null)
                {
                    _logger.LogWarning("Voice state updated event has null user");
                    return;
                }

                // Handle channel creation when user joins a target channel
                if (e.After?.Channel != null)
                {
                    await HandleUserJoinedChannelAsync(e);
                }

                // Handle channel deletion when users leave a created channel
                if (e.Before?.Channel != null)
                {
                    await HandleUserLeftChannelAsync(e);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling voice state update for user {e.User.Id}");
            }
        }

        /// <summary>
        /// Handles when a user joins a voice channel
        /// </summary>
        private async Task HandleUserJoinedChannelAsync(VoiceStateUpdatedEventArgs e)
        {
            try
            {
                // Check if the user joined a join-to-create channel
                foreach (var targetChannelId in _targetChannelIds)
                {
                    if (e.Channel?.Id == targetChannelId && e.After?.Channel != null)
                    {
                        _logger.LogInformation($"User {e.User.Id} ({e.User.Username}) joined join-to-create channel {targetChannelId}");

                        // Create a new voice channel for the user
                        var newChannel = await CreateVoiceChannel(e.Guild, e.Channel, e.After.Member);

                        if (newChannel != null)
                        {
                            // Track the created channel
                            if (!_createdChannels.ContainsKey(targetChannelId))
                            {
                                _createdChannels[targetChannelId] = new List<ulong>();
                            }

                            _createdChannels[targetChannelId].Add(newChannel.Id);
                            _stateManager.AddJtcCreatedChannel(targetChannelId, newChannel.Id);

                            // Move the user to the new channel
                            await e.After.Member.PlaceInAsync(newChannel);

                            _logger.LogInformation($"Created voice channel {newChannel.Id} ({newChannel.Name}) for user {e.User.Id}");
                        }

                        // We only need to process one target channel per event
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling user {e.User.Id} joining a channel");
            }
        }

        /// <summary>
        /// Handles when a user leaves a voice channel
        /// </summary>
        private async Task HandleUserLeftChannelAsync(VoiceStateUpdatedEventArgs e)
        {
            try
            {
                // Check if the user left a dynamic channel
                foreach (var targetChannelId in _targetChannelIds)
                {
                    if (_createdChannels.TryGetValue(targetChannelId, out var channels) &&
                        channels.Contains(e.Before.Channel.Id))
                    {
                        _logger.LogInformation($"User {e.User.Id} left dynamic channel {e.Before.Channel.Id}");

                        // Try to delete the channel if it's now empty
                        try
                        {
                            if (e.Before.Channel != null && e.Before.Channel.Users.Count == 0)
                            {
                                await e.Before.Channel.DeleteAsync();
                                _createdChannels.Remove(e.Before.Channel.Id);
                                _stateManager.RemoveJtcCreatedChannel(targetChannelId, e.Before.Channel.Id);
                                _logger.LogInformation($"{e.Before.Channel.Name} Channel was empty and has been deleted");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error deleting empty join-to-create channel");
                        }

                        // We only need to process one target channel per event
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling user {e.User.Id} leaving a channel");
            }
        }

        /// <summary>
        /// Creates a new voice channel for a user
        /// </summary>
        /// <param name="guild">The Discord guild</param>
        /// <param name="targetChannel">The join-to-create channel</param>
        /// <param name="creator">The Discord member who triggered creation</param>
        /// <returns>The created Discord channel</returns>
        private async Task<DiscordChannel> CreateVoiceChannel(DiscordGuild guild, DiscordChannel targetChannel, DiscordMember creator)
        {
            try
            {
                // Generate channel name
                string channelName = $"{creator.DisplayName}'s Channel";

                // Create the channel
                var newChannel = await guild.CreateVoiceChannelAsync(
                    channelName,
                    targetChannel.Parent,
                    reason: $"Auto-created for {creator.Username}");

                // Give the creator permission to manage the channel
                await newChannel.AddOverwriteAsync(
                    creator,
                    allow: DiscordPermissions.ManageChannels | DiscordPermissions.MoveMembers,
                    reason: "Channel creator permissions");

                _logger.LogInformation($"Created voice channel {newChannel.Id} ({newChannel.Name})");
                return newChannel;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating voice channel for {creator.Id} ({creator.Username})");
                return null;
            }
        }

        /// <summary>
        /// Retrieves all join-to-create channels from the database
        /// </summary>
        /// <returns>A list of join-to-create channels</returns>
        public async Task<List<JoinToCreateChannel>> GetChannels()
        {
            try
            {
                var channels = await _dal.GetAllJoinToCreateChannelsAsync();
                _logger.LogInformation($"Retrieved {channels.Count} join-to-create channels");
                return channels;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving join-to-create channels");
                return new List<JoinToCreateChannel>();
            }
        }

        public async Task<int> DeleteJoinToCreateChannel(long channelId)
        {
            try
            {
                _logger.LogInformation($"Deleting join-to-create channel {channelId}");
                return await _dal.RemoveJoinToCreateChannelAsync(channelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting join-to-create channel {channelId}");
                return 2;
            }
        }
    }
}



