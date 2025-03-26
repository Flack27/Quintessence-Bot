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
    /// <summary>
    /// Data access layer for generating user profile images and leaderboards
    /// </summary>
    public class GenerateImageDAL
    {
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<GenerateImageDAL> _logger;

        /// <summary>
        /// Initializes a new instance of the GenerateImageDAL class
        /// </summary>
        /// <param name="contextFactory">The database context factory</param>
        /// <param name="logger">The logger instance</param>
        public GenerateImageDAL(
            IDbContextFactory<QutieDataTestContext> contextFactory,
            ILogger<GenerateImageDAL> logger)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves user information needed for generating a profile image
        /// </summary>
        /// <param name="userId">The Discord user ID</param>
        /// <returns>An ImageDisplay object containing the user's profile information</returns>
        public async Task<ImageDisplay> GetImageInfoByUserId(long userId)
        {
            _logger.LogInformation($"Retrieving image info for user {userId}");

            try
            {
                using var context = _contextFactory.CreateDbContext();

                var result = await context.Users
                    .Include(u => u.UserData)
                    .Where(u => u.UserId == userId)
                    .Select(u => new ImageDisplay
                    {
                        // User information
                        Name = u.DisplayName,
                        FallBackName = u.UserName,
                        Avatar = u.Avatar,

                        // Voice statistics
                        VoiceLevel = u.UserData.VoiceLevel,
                        VoiceXP = u.UserData.VoiceXp,
                        VoiceReqXP = u.UserData.VoiceRequiredXp,

                        // Message statistics
                        MessageLevel = u.UserData.MessageLevel,
                        MessageXP = u.UserData.MessageXp,
                        MessageReqXP = u.UserData.MessageRequiredXp,

                        // Other stats
                        Karma = u.UserData.Karma,

                        // Rank calculations
                        MessageRank = context.UserData
                            .Count(ud => ud.MessageLevel > u.UserData.MessageLevel ||
                                  (ud.MessageLevel == u.UserData.MessageLevel &&
                                   ud.MessageXp > u.UserData.MessageXp)) + 1,

                        VoiceRank = context.UserData
                            .Count(ud => ud.VoiceLevel > u.UserData.VoiceLevel ||
                                  (ud.VoiceLevel == u.UserData.VoiceLevel &&
                                   ud.VoiceXp > u.UserData.VoiceXp)) + 1
                    })
                    .FirstOrDefaultAsync();

                if (result != null)
                {
                    _logger.LogInformation($"Successfully retrieved image info for user {userId}, " +
                                          $"Voice Level: {result.VoiceLevel}, " +
                                          $"Message Level: {result.MessageLevel}");
                    return result;
                }
                else
                {
                    _logger.LogWarning($"No user data found for user ID {userId}");
                    return new ImageDisplay();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving image info for user {userId}");
                return new ImageDisplay();
            }
        }

        /// <summary>
        /// Retrieves the top 10 users ranked by voice level and XP
        /// </summary>
        /// <returns>A list of the top 10 users by voice activity</returns>
        public async Task<List<ImageDisplay>> GetTopVoiceLevelUsers()
        {
            _logger.LogInformation("Retrieving top voice level users");

            try
            {
                using var context = _contextFactory.CreateDbContext();

                var users = await context.Users
                    .Include(u => u.UserData)
                    .Where(u => u.InGuild == true) // Only include users still in the guild
                    .OrderByDescending(u => u.UserData.VoiceLevel)
                    .ThenByDescending(u => u.UserData.VoiceXp)
                    .Take(10)
                    .ToListAsync();

                var result = users
                    .Select((u, index) => new ImageDisplay
                    {
                        Name = u.DisplayName,
                        FallBackName = u.UserName,
                        Avatar = u.Avatar,
                        VoiceLevel = u.UserData.VoiceLevel,
                        VoiceXP = u.UserData.VoiceXp,
                        VoiceRank = index + 1
                    })
                    .ToList();

                _logger.LogInformation($"Successfully retrieved {result.Count} top voice level users");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving top voice level users");
                return new List<ImageDisplay>();
            }
        }

        /// <summary>
        /// Retrieves the top 10 users ranked by message level and XP
        /// </summary>
        /// <returns>A list of the top 10 users by message activity</returns>
        public async Task<List<ImageDisplay>> GetTopMessageLevelUsers()
        {
            _logger.LogInformation("Retrieving top message level users");

            try
            {
                using var context = _contextFactory.CreateDbContext();

                var users = await context.Users
                    .Include(u => u.UserData)
                    .Where(u => u.InGuild == true) // Only include users still in the guild
                    .OrderByDescending(u => u.UserData.MessageLevel)
                    .ThenByDescending(u => u.UserData.MessageXp)
                    .Take(10)
                    .ToListAsync();

                var result = users
                    .Select((u, index) => new ImageDisplay
                    {
                        Name = u.DisplayName,
                        FallBackName = u.UserName,
                        Avatar = u.Avatar,
                        MessageLevel = u.UserData.MessageLevel,
                        MessageXP = u.UserData.MessageXp,
                        MessageRank = index + 1
                    })
                    .ToList();

                _logger.LogInformation($"Successfully retrieved {result.Count} top message level users");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving top message level users");
                return new List<ImageDisplay>();
            }
        }

        /// <summary>
        /// Retrieves the top 10 users ranked by combined voice and message levels
        /// </summary>
        /// <returns>A list of the top 10 users by overall activity</returns>
        public async Task<List<ImageDisplay>> GetTopLevelUsers()
        {
            _logger.LogInformation("Retrieving top overall level users");

            try
            {
                using var context = _contextFactory.CreateDbContext();

                var users = await context.Users
                    .Include(u => u.UserData)
                    .Where(u => u.InGuild == true) // Only include users still in the guild
                    .Where(u => u.UserData != null) // Ensure UserData exists
                    .Select(u => new
                    {
                        User = u,
                        CombinedLevel = u.UserData.VoiceLevel + u.UserData.MessageLevel,
                        CombinedXP = u.UserData.VoiceXp + u.UserData.MessageXp
                    })
                    .OrderByDescending(x => x.CombinedLevel)
                    .ThenByDescending(x => x.CombinedXP)
                    .Take(10)
                    .ToListAsync();

                var result = users
                    .Select((u, index) => new ImageDisplay
                    {
                        Name = u.User.DisplayName,
                        FallBackName = u.User.UserName,
                        Avatar = u.User.Avatar,
                        // Store combined level in MessageLevel field for display purposes
                        MessageLevel = u.CombinedLevel,
                        // Store combined XP in MessageXP field for display purposes
                        MessageXP = u.CombinedXP,
                        // Store rank in MessageRank field for display purposes
                        MessageRank = index + 1,
                        // Include individual levels for reference
                    })
                    .ToList();

                _logger.LogInformation($"Successfully retrieved {result.Count} top combined level users");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving top combined level users");
                return new List<ImageDisplay>();
            }
        }

        /// <summary>
        /// Gets a user's rank across all three leaderboards
        /// </summary>
        /// <param name="userId">The Discord user ID</param>
        /// <returns>An object containing the user's ranks</returns>
        public async Task<UserRanks> GetUserRanks(long userId)
        {
            _logger.LogInformation($"Retrieving ranks for user {userId}");

            try
            {
                using var context = _contextFactory.CreateDbContext();

                // Get the user data
                var user = await context.Users
                    .Include(u => u.UserData)
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                if (user == null || user.UserData == null)
                {
                    _logger.LogWarning($"No data found for user {userId}");
                    return new UserRanks
                    {
                        VoiceRank = 0,
                        MessageRank = 0,
                        CombinedRank = 0,
                        TotalUsers = await context.Users.CountAsync(u => u.InGuild == true)
                    };
                }

                // Calculate voice rank
                var voiceRank = await context.UserData
                    .CountAsync(ud => ud.User.InGuild == true &&
                               (ud.VoiceLevel > user.UserData.VoiceLevel ||
                               (ud.VoiceLevel == user.UserData.VoiceLevel &&
                                ud.VoiceXp > user.UserData.VoiceXp))) + 1;

                // Calculate message rank
                var messageRank = await context.UserData
                    .CountAsync(ud => ud.User.InGuild == true &&
                               (ud.MessageLevel > user.UserData.MessageLevel ||
                               (ud.MessageLevel == user.UserData.MessageLevel &&
                                ud.MessageXp > user.UserData.MessageXp))) + 1;

                // Calculate combined level and XP for the current user
                var userCombinedLevel = user.UserData.VoiceLevel + user.UserData.MessageLevel;
                var userCombinedXP = user.UserData.VoiceXp + user.UserData.MessageXp;

                // Calculate combined rank
                var combinedRank = await context.Users
                    .Include(u => u.UserData)
                    .Where(u => u.InGuild == true && u.UserData != null)
                    .CountAsync(u =>
                        u.UserData.VoiceLevel + u.UserData.MessageLevel > userCombinedLevel ||
                        (u.UserData.VoiceLevel + u.UserData.MessageLevel == userCombinedLevel &&
                         u.UserData.VoiceXp + u.UserData.MessageXp > userCombinedXP)) + 1;

                // Get total users for percentile calculations
                var totalUsers = await context.Users.CountAsync(u => u.InGuild == true);

                var result = new UserRanks
                {
                    VoiceRank = voiceRank,
                    MessageRank = messageRank,
                    CombinedRank = combinedRank,
                    TotalUsers = totalUsers
                };

                _logger.LogInformation($"Retrieved ranks for user {userId}: " +
                                      $"Voice: {result.VoiceRank}, " +
                                      $"Message: {result.MessageRank}, " +
                                      $"Combined: {result.CombinedRank}, " +
                                      $"Total Users: {result.TotalUsers}");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving ranks for user {userId}");
                return new UserRanks { VoiceRank = 0, MessageRank = 0, CombinedRank = 0, TotalUsers = 0 };
            }
        }
    }

    /// <summary>
    /// Contains a user's ranks across all leaderboards
    /// </summary>
    public class UserRanks
    {
        /// <summary>
        /// User's position in the voice leaderboard
        /// </summary>
        public int VoiceRank { get; set; }

        /// <summary>
        /// User's position in the message leaderboard
        /// </summary>
        public int MessageRank { get; set; }

        /// <summary>
        /// User's position in the combined leaderboard
        /// </summary>
        public int CombinedRank { get; set; }

        /// <summary>
        /// Total number of users in the server
        /// </summary>
        public int TotalUsers { get; set; }
    }
}