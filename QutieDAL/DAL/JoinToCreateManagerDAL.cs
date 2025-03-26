using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QutieDTO.Models;

namespace QutieDAL.DAL
{
    /// <summary>
    /// Data access layer for managing join-to-create channel configurations
    /// </summary>
    public class JoinToCreateManagerDAL
    {
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<JoinToCreateManagerDAL> _logger;

        /// <summary>
        /// Initializes a new instance of the JoinToCreateManagerDAL class
        /// </summary>
        /// <param name="contextFactory">The database context factory</param>
        /// <param name="logger">The logger instance</param>
        public JoinToCreateManagerDAL(
            IDbContextFactory<QutieDataTestContext> contextFactory,
            ILogger<JoinToCreateManagerDAL> logger)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Stores a join-to-create channel in the database
        /// </summary>
        /// <param name="channel">The channel to store</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> StoreJoinToCreateChannelAsync(JoinToCreateChannel channel)
        {
            if (channel == null)
            {
                _logger.LogWarning("Attempted to store null join-to-create channel");
                return false;
            }

            _logger.LogInformation($"Storing join-to-create channel {channel.ChannelId} ({channel.ChannelName}) in category '{channel.Category}'");

            try
            {
                using var context = _contextFactory.CreateDbContext();

                // Check if the channel already exists
                var existingChannel = await context.JoinToCreateChannels
                    .FirstOrDefaultAsync(c => c.ChannelId == channel.ChannelId);

                if (existingChannel == null)
                {
                    // Add new channel
                    context.JoinToCreateChannels.Add(channel);
                    await context.SaveChangesAsync();
                    _logger.LogInformation($"Successfully added new join-to-create channel {channel.ChannelId}");
                }
                else
                {
                    // Update existing channel
                    existingChannel.ChannelName = channel.ChannelName;
                    existingChannel.Category = channel.Category;
                    await context.SaveChangesAsync();
                    _logger.LogInformation($"Updated existing join-to-create channel {channel.ChannelId}");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error storing join-to-create channel {channel.ChannelId}");
                return false;
            }
        }

        /// <summary>
        /// Retrieves all join-to-create channels from the database
        /// </summary>
        /// <returns>A list of join-to-create channels</returns>
        public async Task<List<JoinToCreateChannel>> GetAllJoinToCreateChannelsAsync()
        {
            _logger.LogInformation("Retrieving all join-to-create channels");

            try
            {
                using var context = _contextFactory.CreateDbContext();

                var channels = await context.JoinToCreateChannels.ToListAsync();

                _logger.LogInformation($"Retrieved {channels.Count} join-to-create channels");
                return channels;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving join-to-create channels");
                return new List<JoinToCreateChannel>();
            }
        }

        /// <summary>
        /// Removes a join-to-create channel from the database
        /// </summary>
        /// <param name="channelId">The channel ID to remove</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<int> RemoveJoinToCreateChannelAsync(long channelId)
        {
            _logger.LogInformation($"Removing join-to-create channel {channelId}");

            try
            {
                using var context = _contextFactory.CreateDbContext();

                var channel = await context.JoinToCreateChannels
                    .FirstOrDefaultAsync(c => c.ChannelId == channelId);

                if (channel == null)
                {
                    _logger.LogWarning($"Channel {channelId} not found for removal");
                    return 0;
                }

                context.JoinToCreateChannels.Remove(channel);
                await context.SaveChangesAsync();

                _logger.LogInformation($"Successfully removed join-to-create channel {channelId}");
                return 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing join-to-create channel {channelId}");
                return 2;
            }
        }
    }
}