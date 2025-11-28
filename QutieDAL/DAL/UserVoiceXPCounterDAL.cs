using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace QutieDAL.DAL
{
    /// <summary>
    /// Data access layer for user voice XP functionality
    /// </summary>
    public class UserVoiceXPCounterDAL
    {
        private const long TAX_BANK_USER_ID = 1158671215146315796;
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<UserVoiceXPCounterDAL> _logger;

        /// <summary>
        /// Initializes a new instance of the UserVoiceXPCounterDAL class
        /// </summary>
        /// <param name="contextFactory">The database context factory</param>
        /// <param name="logger">The logger instance</param>
        public UserVoiceXPCounterDAL(
            IDbContextFactory<QutieDataTestContext> contextFactory,
            ILogger<UserVoiceXPCounterDAL> logger)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves the voice XP configuration settings
        /// </summary>
        public async Task<Xpconfig> GetVoiceConfig()
        {
            _logger.LogInformation("Retrieving voice XP configuration");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var config = await context.Xpconfigs.FirstOrDefaultAsync();

                if (config != null)
                {
                    _logger.LogDebug($"Retrieved voice XP config: Min XP {config.VoiceMinXp}, Max XP {config.VoiceMaxXp}, Cooldown {config.VoiceCooldown}");
                    return config;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving voice XP configuration");
            }

            _logger.LogWarning("Returning default voice XP configuration");
            return new Xpconfig();
        }

        /// <summary>
        /// Retrieves a user's voice XP data
        /// </summary>
        /// <param name="userId">The Discord user ID</param>
        public async Task<UserData> GetUserVoiceXP(ulong userId)
        {
            _logger.LogDebug($"Retrieving voice XP data for user {userId}");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var userData = await context.UserData
                    .Include(u => u.User)
                    .FirstOrDefaultAsync(u => u.UserId == (long)userId);

                if (userData == null)
                {
                    _logger.LogInformation($"No voice XP data found for user {userId}, creating new entry");
                    userData = new UserData
                    {
                        UserId = (long)userId,
                        VoiceXp = 0,
                        VoiceLevel = 1,
                        VoiceRequiredXp = 150,
                        TotalVoiceTime = 0,
                        StoredVoiceXp = 0,
                        Karma = 1.0
                    };
                }

                return userData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving voice XP data for user {userId}");
                return null;
            }
        }

        /// <summary>
        /// Retrieves the current voice tax amount from the bank
        /// </summary>
        public async Task<int> GetTax()
        {
            _logger.LogDebug("Retrieving current voice tax amount");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var userData = await context.UserData.FirstOrDefaultAsync(u => u.UserId == TAX_BANK_USER_ID);

                if (userData == null)
                {
                    _logger.LogWarning("Voice tax bank user not found");
                    return 0;
                }

                _logger.LogDebug($"Current voice tax bank balance: {userData.VoiceXp}");
                return userData.VoiceXp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving voice tax amount");
                throw;
            }
        }

        /// <summary>
        /// Adds voice tax amount to the bank
        /// </summary>
        /// <param name="tax">The amount of tax to add</param>
        public async Task SaveTax(int tax)
        {
            if (tax <= 0)
            {
                _logger.LogDebug("Voice tax amount is zero or negative, skipping");
                return;
            }

            _logger.LogDebug($"Adding {tax} XP to voice tax bank");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var userData = await context.UserData.FirstOrDefaultAsync(u => u.UserId == TAX_BANK_USER_ID);

                if (userData != null)
                {
                    userData.VoiceXp += tax;
                    await context.SaveChangesAsync();
                    _logger.LogDebug($"Voice tax bank new balance: {userData.VoiceXp}");
                }
                else
                {
                    _logger.LogWarning("Voice tax bank user not found, creating new entry");

                    var newBank = new UserData
                    {
                        UserId = TAX_BANK_USER_ID,
                        VoiceXp = tax,
                        VoiceLevel = 1,
                        VoiceRequiredXp = 150,
                        TotalVoiceTime = 0,
                        StoredVoiceXp = 0,
                        Karma = 1.0
                    };

                    await context.UserData.AddAsync(newBank);
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving voice tax amount of {tax}");
            }
        }

        /// <summary>
        /// Removes voice tax amount from the bank
        /// </summary>
        /// <param name="tax">The amount of tax to withdraw</param>
        public async Task WithdrawTax(int tax)
        {
            if (tax <= 0)
            {
                _logger.LogDebug("Voice tax withdrawal amount is zero or negative, skipping");
                return;
            }

            _logger.LogInformation($"Withdrawing {tax} XP from voice tax bank");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var userData = await context.UserData.FirstOrDefaultAsync(u => u.UserId == TAX_BANK_USER_ID);

                if (userData != null)
                {
                    userData.VoiceXp -= tax;
                    if (userData.VoiceXp < 0)
                    {
                        _logger.LogWarning($"Voice tax bank balance went negative ({userData.VoiceXp}), setting to 0");
                        userData.VoiceXp = 0;
                    }

                    await context.SaveChangesAsync();
                    _logger.LogDebug($"Voice tax bank new balance after withdrawal: {userData.VoiceXp}");
                }
                else
                {
                    _logger.LogWarning("Voice tax bank user not found during withdrawal");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error withdrawing voice tax amount of {tax}");
            }
        }

        /// <summary>
        /// Saves a user's voice XP data
        /// </summary>
        /// <param name="userXP">The user data to save</param>
        public async Task SaveUserVoiceXP(UserData userXP)
        {
            _logger.LogDebug($"Saving voice XP data for user {userXP.UserId}");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var existingUser = await context.UserData.FirstOrDefaultAsync(u => u.UserId == userXP.UserId);

                if (existingUser != null)
                {
                    _logger.LogDebug($"Updating existing user {userXP.UserId}: Voice XP {userXP.VoiceXp}, Level {userXP.VoiceLevel}, Total Time {userXP.TotalVoiceTime}");

                    existingUser.VoiceXp = userXP.VoiceXp;
                    existingUser.VoiceLevel = userXP.VoiceLevel;
                    existingUser.VoiceRequiredXp = userXP.VoiceRequiredXp;
                    existingUser.TotalVoiceTime = userXP.TotalVoiceTime;
                    existingUser.StoredVoiceXp = userXP.StoredVoiceXp;
                    existingUser.Karma = userXP.Karma;
                }
                else
                {
                    _logger.LogInformation($"Creating new voice user data for user {userXP.UserId}");

                    var newUser = new UserData
                    {
                        UserId = userXP.UserId,
                        VoiceXp = userXP.VoiceXp,
                        VoiceLevel = userXP.VoiceLevel,
                        VoiceRequiredXp = userXP.VoiceRequiredXp,
                        TotalVoiceTime = userXP.TotalVoiceTime,
                        StoredVoiceXp = userXP.StoredVoiceXp,
                        Karma = userXP.Karma
                    };
                    await context.UserData.AddAsync(newUser);
                }
                await context.SaveChangesAsync();
                _logger.LogDebug($"Successfully saved voice XP data for user {userXP.UserId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving voice XP data for user {userXP.UserId}");
            }
        }

        /// <summary>
        /// Clears all users' stored voice XP
        /// </summary>
        public async Task WipeBankData()
        {
            _logger.LogWarning("Wiping all users' stored voice XP data");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var allUserData = await context.UserData.ToListAsync();

                _logger.LogInformation($"Wiping stored voice XP for {allUserData.Count} users");

                foreach (var userData in allUserData)
                {
                    userData.StoredVoiceXp = 0;
                }

                await context.SaveChangesAsync();
                _logger.LogInformation("All users' stored voice XP has been wiped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error wiping voice bank data");
            }
        }

        /// <summary>
        /// Gets all level-to-role voice mappings
        /// </summary>
        public async Task<List<LevelToRoleVoice>> GetLevelRoleVoice()
        {
            _logger.LogInformation("Retrieving level-to-role voice mappings");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var mappings = await context.LevelToRoleVoices.ToListAsync();
                _logger.LogDebug($"Retrieved {mappings.Count} level-to-role voice mappings");
                return mappings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving level-to-role voice mappings");
                return new List<LevelToRoleVoice>();
            }
        }


        public async Task UpdateUserVoiceActivityForDate(ulong userId, decimal voiceMinutes, int xpEarned, DateTime date)
        {
            try
            {
                var targetDate = date.Date;

                using var context = _contextFactory.CreateDbContext();
                var summary = await context.UserVoiceActivitySummary
                    .FirstOrDefaultAsync(s => s.UserId == (long)userId && s.Date == targetDate);

                if (summary == null)
                {
                    summary = new UserVoiceActivitySummary
                    {
                        UserId = (long)userId,
                        Date = targetDate,
                        VoiceMinutes = voiceMinutes,
                        XpEarned = xpEarned
                    };
                    await context.UserVoiceActivitySummary.AddAsync(summary);
                }
                else
                {
                    summary.VoiceMinutes += voiceMinutes;
                    summary.XpEarned += xpEarned;
                }

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating voice activity for user {userId} on {date:yyyy-MM-dd}");
            }
        }
    }
}