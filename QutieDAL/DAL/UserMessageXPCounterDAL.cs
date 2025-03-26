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
    /// Data access layer for user message XP functionality
    /// </summary>
    public class UserMessageXPCounterDAL
    {
        private const long TAX_BANK_USER_ID = 1158671215146315796;
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<UserMessageXPCounterDAL> _logger;

        /// <summary>
        /// Initializes a new instance of the UserMessageXPCounterDAL class
        /// </summary>
        /// <param name="contextFactory">The database context factory</param>
        /// <param name="logger">The logger instance</param>
        public UserMessageXPCounterDAL(
            IDbContextFactory<QutieDataTestContext> contextFactory,
            ILogger<UserMessageXPCounterDAL> logger)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves the XP configuration settings
        /// </summary>
        public async Task<Xpconfig> GetMessageConfig()
        {
            _logger.LogInformation("Retrieving message XP configuration");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var config = await context.Xpconfigs.FirstAsync();

                if (config != null)
                {
                    _logger.LogDebug($"Retrieved XP config: Min XP {config.MessageMinXp}, Max XP {config.MessageMaxXp}, Cooldown {config.MessageCooldown}s");
                    return config;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving message XP configuration");
            }

            _logger.LogWarning("Returning default XP configuration");
            return new Xpconfig();
        }

        /// <summary>
        /// Retrieves a user's message XP data
        /// </summary>
        /// <param name="userId">The Discord user ID</param>
        public async Task<UserData> GetUserMessageXP(ulong userId)
        {
            _logger.LogDebug($"Retrieving message XP data for user {userId}");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var userData = await context.UserData
                    .Include(u => u.User)
                    .FirstOrDefaultAsync(u => u.UserId == (long)userId);

                if (userData == null)
                {
                    _logger.LogInformation($"No XP data found for user {userId}, creating new entry");
                    userData = new UserData
                    {
                        UserId = (long)userId,
                        MessageXp = 0,
                        MessageLevel = 1,
                        MessageRequiredXp = 150,
                        MessageCount = 0,
                        StoredMessageXp = 0,
                        Karma = 1.0
                    };
                }

                return userData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving message XP data for user {userId}");
                return null;
            }
        }

        /// <summary>
        /// Retrieves the current tax amount from the bank
        /// </summary>
        public async Task<int> GetTax()
        {
            _logger.LogDebug("Retrieving current tax amount");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var userData = await context.UserData.FirstOrDefaultAsync(u => u.UserId == TAX_BANK_USER_ID);

                if (userData == null)
                {
                    _logger.LogWarning("Tax bank user not found");
                    return 0;
                }

                _logger.LogDebug($"Current tax bank balance: {userData.MessageXp}");
                return userData.MessageXp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tax amount");
                throw;
            }
        }

        /// <summary>
        /// Saves a user's message XP data
        /// </summary>
        /// <param name="userXP">The user data to save</param>
        public async Task SaveUserMessageXP(UserData userXP)
        {
            _logger.LogDebug($"Saving message XP data for user {userXP.UserId}");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var existingUser = await context.UserData.FirstOrDefaultAsync(u => u.UserId == userXP.UserId);

                if (existingUser != null)
                {
                    _logger.LogDebug($"Updating existing user {userXP.UserId}: XP {userXP.MessageXp}, Level {userXP.MessageLevel}, Karma {userXP.Karma:F2}");

                    existingUser.MessageXp = userXP.MessageXp;
                    existingUser.MessageLevel = userXP.MessageLevel;
                    existingUser.MessageRequiredXp = userXP.MessageRequiredXp;
                    existingUser.MessageCount = userXP.MessageCount;
                    existingUser.StoredMessageXp = userXP.StoredMessageXp;
                    existingUser.Karma = userXP.Karma;
                }
                else
                {
                    _logger.LogInformation($"Creating new user data for user {userXP.UserId}");

                    var newUser = new UserData
                    {
                        UserId = userXP.UserId,
                        MessageXp = userXP.MessageXp,
                        MessageLevel = userXP.MessageLevel,
                        MessageRequiredXp = userXP.MessageRequiredXp,
                        MessageCount = userXP.MessageCount,
                        StoredMessageXp = userXP.StoredMessageXp,
                        Karma = userXP.Karma
                    };
                    await context.UserData.AddAsync(newUser);
                }

                await context.SaveChangesAsync();
                _logger.LogDebug($"Successfully saved XP data for user {userXP.UserId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving message XP data for user {userXP.UserId}");
            }
        }

        /// <summary>
        /// Adds tax amount to the bank
        /// </summary>
        /// <param name="tax">The amount of tax to add</param>
        public async Task SaveTax(int tax)
        {
            if (tax <= 0)
            {
                _logger.LogDebug("Tax amount is zero or negative, skipping");
                return;
            }

            _logger.LogDebug($"Adding {tax} XP to tax bank");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var userData = await context.UserData.FirstOrDefaultAsync(u => u.UserId == TAX_BANK_USER_ID);

                if (userData != null)
                {
                    userData.MessageXp += tax;
                    await context.SaveChangesAsync();
                    _logger.LogDebug($"Tax bank new balance: {userData.MessageXp}");
                }
                else
                {
                    _logger.LogWarning("Tax bank user not found, creating new entry");

                    var newBank = new UserData
                    {
                        UserId = TAX_BANK_USER_ID,
                        MessageXp = tax,
                        MessageLevel = 1,
                        MessageRequiredXp = 150,
                        MessageCount = 0,
                        StoredMessageXp = 0,
                        Karma = 1.0
                    };

                    await context.UserData.AddAsync(newBank);
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving tax amount of {tax}");
            }
        }

        /// <summary>
        /// Removes tax amount from the bank
        /// </summary>
        /// <param name="tax">The amount of tax to withdraw</param>
        public async Task WithdrawTax(int tax)
        {
            if (tax <= 0)
            {
                _logger.LogDebug("Tax withdrawal amount is zero or negative, skipping");
                return;
            }

            _logger.LogInformation($"Withdrawing {tax} XP from tax bank");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var userData = await context.UserData.FirstOrDefaultAsync(u => u.UserId == TAX_BANK_USER_ID);

                if (userData != null)
                {
                    userData.MessageXp -= tax;
                    if (userData.MessageXp < 0)
                    {
                        _logger.LogWarning($"Tax bank balance went negative ({userData.MessageXp}), setting to 0");
                        userData.MessageXp = 0;
                    }

                    await context.SaveChangesAsync();
                    _logger.LogDebug($"Tax bank new balance after withdrawal: {userData.MessageXp}");
                }
                else
                {
                    _logger.LogWarning("Tax bank user not found during withdrawal");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error withdrawing tax amount of {tax}");
            }
        }

        /// <summary>
        /// Clears all users' stored XP
        /// </summary>
        public async Task WipeBankData()
        {
            _logger.LogWarning("Wiping all users' stored XP data");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var allUserData = await context.UserData.ToListAsync();

                _logger.LogInformation($"Wiping stored XP for {allUserData.Count} users");

                foreach (var userData in allUserData)
                {
                    userData.StoredMessageXp = 0;
                }

                await context.SaveChangesAsync();
                _logger.LogInformation("All users' stored XP has been wiped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error wiping bank data");
            }
        }

        /// <summary>
        /// Gets all level-to-role mappings
        /// </summary>
        public async Task<List<LevelToRoleMessage>> GetLevelRoleMessages()
        {
            _logger.LogInformation("Retrieving level-to-role mappings");
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var mappings = await context.LevelToRoleMessages.ToListAsync();
                _logger.LogDebug($"Retrieved {mappings.Count} level-to-role mappings");
                return mappings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving level-to-role mappings");
                return new List<LevelToRoleMessage>();
            }
        }

        public async Task UpdateUserMessageActivity(ulong userId, int xpEarned)
        {
            try
            {
                var today = DateTime.UtcNow.Date; // Just get the date part

                using var context = _contextFactory.CreateDbContext();
                var summary = await context.UserMessageActivitySummary
                    .FirstOrDefaultAsync(s => s.UserId == (long)userId && s.Date == today);

                if (summary == null)
                {
                    // Create new day record
                    summary = new UserMessageActivitySummary
                    {
                        UserId = (long)userId,
                        Date = today,
                        MessageCount = 1,
                        XpEarned = xpEarned
                    };
                    await context.UserMessageActivitySummary.AddAsync(summary);
                }
                else
                {
                    // Update existing day record
                    summary.MessageCount++;
                    summary.XpEarned += xpEarned;
                }

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating message activity for user {userId}");
            }
        }
    }
}