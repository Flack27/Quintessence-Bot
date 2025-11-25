using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QutieDTO;
using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QutieDAL.DAL
{
    public class CommandsDAL
    {
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<CommandsDAL> _logger;

        public CommandsDAL(
            IDbContextFactory<QutieDataTestContext> contextFactory,
            ILogger<CommandsDAL> logger)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> SaveGameData(Game game)
        {
            try
            {
                if (game == null)
                {
                    _logger.LogWarning("Attempted to save null game data");
                    return false;
                }

                _logger.LogInformation($"Saving game data for {game.GameName}");

                using var context = _contextFactory.CreateDbContext();
                context.Games.Add(game);
                await context.SaveChangesAsync();

                _logger.LogInformation($"Successfully saved game data for {game.GameName} with ID {game.GameId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving game data for {game?.GameName}");
                return false;
            }
        }

        public async Task<bool> RemoveGame(long gameId)
        {
            try
            {
                _logger.LogInformation($"Removing game with ID {gameId}");

                using var context = _contextFactory.CreateDbContext();
                var game = await context.Games.FirstOrDefaultAsync(g => g.GameId == gameId);

                if (game == null)
                {
                    _logger.LogWarning($"Game with ID {gameId} not found for deletion");
                    return false;
                }

                context.Games.Remove(game);
                await context.SaveChangesAsync();

                _logger.LogInformation($"Successfully removed game {game.GameName} with ID {gameId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing game with ID {gameId}");
                return false;
            }
        }

        public async Task<List<Game>?> ShowGameData()
        {
            try
            {
                _logger.LogInformation("Retrieving all games data");

                using var context = _contextFactory.CreateDbContext();
                var games = await context.Games
                    .Include(g => g.Role)
                    .Include(g => g.Channel)
                    .Include(g => g.Channels)
                    .ToListAsync();

                _logger.LogInformation($"Retrieved {games.Count} games");
                return games;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving game data");
                return null;
            }
        }

        public async Task<Event?> GetEventWithChannel(long eventId)
        {
            try
            {
                _logger.LogInformation($"Retrieving event {eventId} with channel data");

                using var context = _contextFactory.CreateDbContext();
                var evt = await context.Events
                    .Include(e => e.Channel)
                    .ThenInclude(c => c.Game)
                    .FirstOrDefaultAsync(e => e.EventId == eventId);

                if (evt == null)
                {
                    _logger.LogWarning($"Event with ID {eventId} not found");
                    return null;
                }

                _logger.LogInformation($"Successfully retrieved event {evt.Title} with ID {eventId}");
                return evt;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving event with ID {eventId}");
                return null;
            }
        }


        /// <summary>
        /// Saves a new reaction role configuration or updates an existing one
        /// </summary>
        public async Task<bool> SaveReactionRole(long channelId, long messageId, string emojiName, long emojiId, long roleId)
        {
            try
            {
                _logger.LogInformation($"Saving reaction role for message {messageId} in channel {channelId}");

                using var context = _contextFactory.CreateDbContext();

                _logger.LogInformation($"Checking for existing: Channel={channelId}, Message={messageId}, Name='{emojiName}', Id={emojiId}");

                // Check if this reaction role already exists
                var existingReactionRole = await context.ReactionRoles
                    .FirstOrDefaultAsync(r =>
                        r.ChannelId == channelId &&
                        r.MessageId == messageId &&
                        r.EmojiId == emojiId &&
                        EF.Functions.Collate(r.EmojiName, "Latin1_General_BIN2") == EF.Functions.Collate(emojiName, "Latin1_General_BIN2"));

                if (existingReactionRole != null)
                {
                    // Update existing role
                    existingReactionRole.RoleId = roleId;
                    context.ReactionRoles.Update(existingReactionRole);

                    _logger.LogInformation($"Found match: ID={existingReactionRole.Id}, Name='{existingReactionRole.EmojiName}', EmojiId={existingReactionRole.EmojiId}");
                }
                else
                {
                    // Create new reaction role
                    var newReactionRole = new ReactionRoles
                    {
                        ChannelId = channelId,
                        MessageId = messageId,
                        EmojiName = emojiName,
                        EmojiId = emojiId,
                        RoleId = roleId
                    };

                    context.ReactionRoles.Add(newReactionRole);

                    _logger.LogInformation($"Created new reaction role for emoji {emojiName} to assign role {roleId}");
                }

                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving reaction role for message {messageId} in channel {channelId}");
                return false;
            }
        }

        /// <summary>
        /// Removes a reaction role configuration from the database
        /// </summary>
        public async Task<bool> RemoveReactionRole(long channelId, long messageId, string emojiName, long emojiId)
        {
            try
            {
                _logger.LogInformation($"Removing reaction role for emoji {emojiName} on message {messageId}");

                using var context = _contextFactory.CreateDbContext();

                var reactionRole = await context.ReactionRoles
                    .FirstOrDefaultAsync(r =>
                        r.ChannelId == channelId &&
                        r.MessageId == messageId &&
                        r.EmojiId == emojiId &&
                        EF.Functions.Collate(r.EmojiName, "Latin1_General_BIN2") == EF.Functions.Collate(emojiName, "Latin1_General_BIN2"));

                if (reactionRole == null)
                {
                    _logger.LogWarning($"No reaction role found for emoji {emojiName} on message {messageId}");
                    return false;
                }

                context.ReactionRoles.Remove(reactionRole);
                await context.SaveChangesAsync();

                _logger.LogInformation($"Successfully removed reaction role (ID: {reactionRole.Id})");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing reaction role for message {messageId} in channel {channelId}");
                return false;
            }
        }

        /// <summary>
        /// Gets a specific reaction role configuration from the database
        /// </summary>
        public async Task<ReactionRoles> GetReactionRole(long channelId, long messageId, string emojiName, long emojiId)
        {
            try
            {
                _logger.LogInformation($"Getting reaction role for emoji {emojiName} on message {messageId}");

                using var context = _contextFactory.CreateDbContext();

                var reactionRole = await context.ReactionRoles
                    .FirstOrDefaultAsync(r =>
                        r.ChannelId == channelId &&
                        r.MessageId == messageId &&
                        r.EmojiId == emojiId &&
                        EF.Functions.Collate(r.EmojiName, "Latin1_General_BIN2") == EF.Functions.Collate(emojiName, "Latin1_General_BIN2"));

                if (reactionRole != null)
                {
                    _logger.LogInformation($"Found reaction role (ID: {reactionRole.Id}) for emoji {emojiName}");
                }
                else
                {
                    _logger.LogInformation($"No reaction role found for emoji {emojiName} on message {messageId}");
                }

                return reactionRole;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving reaction role for message {messageId} in channel {channelId}");
                return null;
            }
        }

        /// <summary>
        /// Gets all reaction roles in the database
        /// </summary>
        public async Task<List<ReactionRoles>> GetAllReactionRoles()
        {
            try
            {
                _logger.LogInformation("Retrieving all reaction roles");

                using var context = _contextFactory.CreateDbContext();

                var reactionRoles = await context.ReactionRoles.ToListAsync();

                _logger.LogInformation($"Retrieved {reactionRoles.Count} reaction roles");

                return reactionRoles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all reaction roles");
                return new List<ReactionRoles>();
            }
        }

        /// <summary>
        /// Gets all reaction roles for a specific message
        /// </summary>
        public async Task<List<ReactionRoles>> GetReactionRolesForMessage(long channelId, long messageId)
        {
            try
            {
                _logger.LogInformation($"Retrieving reaction roles for message {messageId} in channel {channelId}");

                using var context = _contextFactory.CreateDbContext();

                var reactionRoles = await context.ReactionRoles
                    .Where(r => r.ChannelId == channelId && r.MessageId == messageId)
                    .ToListAsync();

                _logger.LogInformation($"Retrieved {reactionRoles.Count} reaction roles for message {messageId}");

                return reactionRoles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving reaction roles for message {messageId}");
                return new List<ReactionRoles>();
            }
        }

        /// <summary>
        /// Gets all reaction roles that assign a specific role
        /// </summary>
        public async Task<List<ReactionRoles>> GetReactionRolesByRoleId(long roleId)
        {
            try
            {
                _logger.LogInformation($"Retrieving reaction roles for role {roleId}");

                using var context = _contextFactory.CreateDbContext();

                var reactionRoles = await context.ReactionRoles
                    .Where(r => r.RoleId == roleId)
                    .ToListAsync();

                _logger.LogInformation($"Retrieved {reactionRoles.Count} reaction roles for role {roleId}");

                return reactionRoles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving reaction roles for role {roleId}");
                return new List<ReactionRoles>();
            }
        }
    }
}