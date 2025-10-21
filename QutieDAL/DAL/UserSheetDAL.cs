using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace QutieDAL.DAL
{
    public class UserSheetDAL
    {
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<UserSheetDAL> _logger;

        public UserSheetDAL(IDbContextFactory<QutieDataTestContext> contextFactory, ILogger<UserSheetDAL> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        // Helper method to get the game-specific data entity
        private async Task<object?> GetGameDataEntityAsync(QutieDataTestContext context, long userId, long gameId)
        {
            switch (gameId)
            {
                case 4: // Ashes of Creation
                    return await context.AocData.FirstOrDefaultAsync(d => d.UserId == userId);
                case 7: // WWM
                    return await context.WwmData.FirstOrDefaultAsync(d => d.UserId == userId);
                case 8: // AION
                    return await context.AionData.FirstOrDefaultAsync(d => d.UserId == userId);
                default:
                    _logger.LogWarning($"No data mapping configured for game ID {gameId}");
                    return null;
            }
        }

        public async Task<List<string>?> GetUserGameData(long userId, long gameId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();

                // Get user and game basic info
                var user = await context.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.UserId == userId);
                var game = await context.Games.Include(g => g.Channels).FirstOrDefaultAsync(g => g.GameId == gameId);

                if (user == null || game == null)
                {
                    _logger.LogWarning($"User {userId} or game {gameId} not found");
                    return null;
                }

                // Find user's channel role
                var channelRoleIds = game.Channels.Select(c => c.RoleId).ToList();
                var role = user.Roles.FirstOrDefault(role => channelRoleIds.Contains(role.RoleId));

                // Get the game-specific data entity
                var gameData = await GetGameDataEntityAsync(context, userId, gameId);
                if (gameData == null)
                {
                    // Return basic user data even if no game-specific data exists
                    return new List<string> { user.DisplayName ?? "", "", role?.RoleName ?? "" };
                }

                // Get field definitions for this game
                var fields = await context.GameFieldDefinition
                    .Where(f => f.GameId == gameId)
                    .OrderBy(f => f.DisplayOrder)
                    .ToListAsync();

                // Start with common fields
                var result = new List<string>
                {
                    user.DisplayName ?? "",
                    GetPropertyValue(gameData, "IGN") ?? "",
                    role?.RoleName ?? ""
                };

                // Add all other field values in the defined order
                foreach (var field in fields.Where(f => f.FieldName != "IGN")) // Skip IGN as we already added it
                {
                    result.Add(GetPropertyValue(gameData, field.FieldName) ?? "");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting game data for user {userId} in game {gameId}");
                return null;
            }
        }

        public async Task<List<string>?> GetUserChannelData(long userId, long channelId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();

                // Get channel data
                var channel = await context.Channels
                    .Include(c => c.Role)
                    .Include(c => c.Game)
                    .FirstOrDefaultAsync(c => c.ChannelId == channelId);

                if (channel == null)
                {
                    _logger.LogWarning($"Channel with ID {channelId} not found");
                    return null;
                }

                // Get user data
                var user = await context.Users
                    .Include(u => u.Roles)
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                if (user == null)
                {
                    _logger.LogWarning($"User with ID {userId} not found");
                    return null;
                }

                // Get the game data entity
                var gameData = await GetGameDataEntityAsync(context, userId, channel.Game.GameId);

                // Create result
                var result = new List<string>
                {
                    user.DisplayName ?? "",
                    GetPropertyValue(gameData, "IGN") ?? "",
                    channel.Role?.RoleName ?? ""
                };

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting user data for user {userId} in channel {channelId}");
                return null;
            }
        }

        // Helper method to get a property value using reflection
        private string? GetPropertyValue(object? obj, string propertyName)
        {
            if (obj == null) return null;

            var property = obj.GetType().GetProperty(propertyName);
            if (property == null) return null;

            var value = property.GetValue(obj);
            return value?.ToString();
        }

        public async Task<List<long>> GetUserIdsWithGameRoleAsync(long gameId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();

                var game = await context.Games.FirstOrDefaultAsync(g => g.GameId == gameId);
                if (game == null)
                {
                    _logger.LogWarning($"Game with ID {gameId} not found");
                    return new List<long>();
                }

                var gameRoleId = game.RoleId;
                var userIds = await context.Roles
                    .Where(role => role.RoleId == gameRoleId)
                    .SelectMany(role => role.Users.Select(ur => ur.UserId))
                    .Distinct()
                    .ToListAsync();

                return userIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting user IDs with game role for game {gameId}");
                return new List<long>();
            }
        }

        public async Task<List<long>> GetUserIdsWithRoleAsync(long roleId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();

                var userIds = await context.Roles
                    .Where(role => role.RoleId == roleId)
                    .SelectMany(role => role.Users.Select(ur => ur.UserId))
                    .Distinct()
                    .ToListAsync();

                return userIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting user IDs with role {roleId}");
                return new List<long>();
            }
        }
    }
}