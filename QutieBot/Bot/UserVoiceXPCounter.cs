using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using QutieDAL.DAL;
using QutieDTO.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QutieBot.Bot
{
    /// <summary>
    /// Manages user experience points for voice channel activity
    /// </summary>
    public class UserVoiceXPCounter
    {
        private readonly UserVoiceXPCounterDAL _databaseManager;
        private readonly ILogger<UserVoiceXPCounter> _logger;
        private DiscordChannel _levelUpChannel;
        private readonly Random _random = new Random();

        private readonly ConcurrentDictionary<ulong, DateTime> _voiceActivityStartTimes = new ConcurrentDictionary<ulong, DateTime>();

        // Constants for Discord IDs and other configuration
        private const ulong LEVEL_UP_CHANNEL_ID = 1151618049250709645;
        private const ulong AFK_VOICE_CHANNEL_ID = 1174080409949180035;
        private const ulong LEADERSHIP_ROLE_ID = 1152617541190041600;
        private const ulong TAX_BANK_USER_ID = 1158671215146315796;
        private const double CRINGE_MESSAGE_CHANCE = 0.01;

        /// <summary>
        /// Initializes a new instance of the UserVoiceXPCounter class
        /// </summary>
        /// <param name="databaseManager">The database manager for user voice XP</param>
        /// <param name="logger">The logger instance</param>
        public UserVoiceXPCounter(
            UserVoiceXPCounterDAL databaseManager,
            ILogger<UserVoiceXPCounter> logger)
        {
            _databaseManager = databaseManager ?? throw new ArgumentNullException(nameof(databaseManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Handles voice state update events to award XP for voice activity
        /// </summary>
        /// <param name="client">The Discord client that raised the event</param>
        /// <param name="e">Event arguments containing voice state information</param>
        public async Task VoiceStateUpdated(DiscordClient client, VoiceStateUpdatedEventArgs e)
        {
            try
            {
                _levelUpChannel = await client.GetChannelAsync(LEVEL_UP_CHANNEL_ID);

                // Skip if user is self-deafened or in AFK channel
                if (e.After != null && (e.After.IsSelfDeafened || e.After.Channel?.Id == AFK_VOICE_CHANNEL_ID))
                {
                    _logger.LogDebug($"Skipping voice XP for user {e.User.Id} - self-deafened or in AFK channel");
                    return;
                }

                // User joined a voice channel
                if (e.After != null && e.Before == null && e.After.Channel != null)
                {
                    await HandleUserJoinedVoice(e);
                }

                // User left a voice channel
                if (e.Before != null && e.Before.Channel != null && (e.After == null || e.After.Channel == null))
                {
                    await HandleUserLeftVoice(e);
                }

                // User switched voice channels
                if (e.After != null && e.Before != null && e.After.Channel != e.Before.Channel && e.After.Channel != null)
                {
                    await HandleUserSwitchedVoice(e);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing voice state update for user {e.User.Id}");
            }
        }

        /// <summary>
        /// Handles when a user joins a voice channel
        /// </summary>
        private async Task HandleUserJoinedVoice(VoiceStateUpdatedEventArgs e)
        {
            try
            {
                if (e.User == null)
                {
                    _logger.LogWarning("Null user in HandleUserJoinedVoice");
                    return;
                }

                if (_voiceActivityStartTimes.TryAdd(e.User.Id, DateTime.Now))
                {
                    _logger.LogInformation($"User {e.User.Username} ({e.User.Id}) joined voice channel {e.After.Channel.Name} - tracking started");
                }
                else
                {
                    _voiceActivityStartTimes[e.User.Id] = DateTime.Now;
                    _logger.LogDebug($"Updated existing voice tracking for user {e.User.Username} ({e.User.Id})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in HandleUserJoinedVoice for user ID: {(e.User != null ? e.User.Id.ToString() : "null")}, Username: {(e.User != null ? e.User.Username : "null")}");
            }
        }

        /// <summary>
        /// Handles when a user leaves a voice channel
        /// </summary>
        private async Task HandleUserLeftVoice(VoiceStateUpdatedEventArgs e)
        {
            try
            {
                if (e.User == null)
                {
                    _logger.LogWarning("Null user in HandleUserLeftVoice");
                    return;
                }

                // Try to remove the entry and get the start time
                if (_voiceActivityStartTimes.TryRemove(e.User.Id, out DateTime startTime))
                {
                    _logger.LogInformation($"User {e.User.Username} ({e.User.Id}) left voice channel {e.Before.Channel.Name}");

                    UserData userVoiceXP = await _databaseManager.GetUserVoiceXP(e.User.Id);
                    var timeElapsed = DateTime.Now - startTime;

                    // Skip if time spent was too short (less than 1 minute)
                    if (timeElapsed.TotalMinutes < 1)
                    {
                        _logger.LogDebug($"User {e.User.Id} spent less than 1 minute in voice - no XP awarded");
                        return;
                    }

                    await ProcessVoiceXPEarning(e, userVoiceXP, timeElapsed);
                }
                else
                {
                    _logger.LogDebug($"User {e.User.Username} ({e.User.Id}) left voice but was not being tracked");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in HandleUserLeftVoice for user ID: {(e.User != null ? e.User.Id.ToString() : "null")}, Username: {(e.User != null ? e.User.Username : "null")}");
            }
        }

        /// <summary>
        /// Handles when a user switches voice channels
        /// </summary>
        private async Task HandleUserSwitchedVoice(VoiceStateUpdatedEventArgs e)
        {
            try
            {
                if (e.User == null)
                {
                    _logger.LogWarning("Null user in HandleUserSwitchedVoice");
                    return;
                }

                DateTime startTime = _voiceActivityStartTimes.GetOrAdd(e.User.Id, DateTime.Now);

                if ((DateTime.Now - startTime).TotalMilliseconds < 100) // If timestamp is very recent (new addition)
                {
                    _logger.LogDebug($"User {e.User.Username} ({e.User.Id}) switched to voice channel {e.After.Channel.Name} - tracking started");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in HandleUserSwitchedVoice for user ID: {(e.User != null ? e.User.Id.ToString() : "null")}, Username: {(e.User != null ? e.User.Username : "null")}");
            }
        }

        /// <summary>
        /// Processes XP earning for voice activity
        /// </summary>
        private async Task ProcessVoiceXPEarning(VoiceStateUpdatedEventArgs e, UserData userVoiceXP, TimeSpan timeElapsed)
        {
            var config = await _databaseManager.GetVoiceConfig();
            var randomXP = _random.Next(config.VoiceMinXp, config.VoiceMaxXp + 1);
            var xpEarned = (int)(timeElapsed.TotalMinutes / config.VoiceCooldown) * randomXP;

            _logger.LogDebug($"User {e.User.Username} ({e.User.Id}) spent {timeElapsed.TotalMinutes:F1} minutes in voice");
            _logger.LogDebug($"Base XP earned: {xpEarned} (based on {randomXP} XP per {config.VoiceCooldown} minutes)");

            var guild = e.Guild;
            var guildMember = await guild.GetMemberAsync(e.User.Id);
            bool isLeadership = guildMember.Roles.Any(role => role.Id == LEADERSHIP_ROLE_ID);

            // Apply tax if not leadership
            double tax = await GetTax();
            var taxDeduction = (int)(xpEarned * tax);

            if (!isLeadership)
            {
                _logger.LogDebug($"Applying tax rate of {tax:P} to user {e.User.Id}. Tax: {taxDeduction} XP");
                xpEarned -= taxDeduction;
            }

            await _databaseManager.SaveTax(taxDeduction);

            // Update karma if needed
            if (userVoiceXP.Karma < 1)
            {
                double karmaIncrease = xpEarned * 0.0001 * Math.Pow(userVoiceXP.Karma, 2);
                userVoiceXP.Karma += karmaIncrease;

                if (userVoiceXP.Karma > 1)
                {
                    userVoiceXP.Karma = 1;
                }

                _logger.LogDebug($"User {e.User.Id} karma increased by {karmaIncrease:F4} to {userVoiceXP.Karma:F4}");
            }

            // Apply karma modifier to XP
            xpEarned = (int)(xpEarned * userVoiceXP.Karma);
            userVoiceXP.VoiceXp += xpEarned;
            userVoiceXP.TotalVoiceTime += Math.Round((decimal)(timeElapsed.TotalMinutes / 60.0), 2);

            await _databaseManager.UpdateUserVoiceActivity(e.User.Id, (decimal)timeElapsed.TotalMinutes, xpEarned);
            _logger.LogInformation($"User {e.User.Username} ({e.User.Id}) earned {xpEarned} voice XP (after karma modifier: {userVoiceXP.Karma:F2})");

            await CalculateLevelAndRequiredXP(userVoiceXP, e.User);
        }

        /// <summary>
        /// Calculates user level based on XP and handles level-up events
        /// </summary>
        private async Task CalculateLevelAndRequiredXP(UserData userVoiceXP, DiscordUser user)
        {
            List<LevelToRoleVoice> levelRoleMappings = await _databaseManager.GetLevelRoleVoice();
            int initialLevel = userVoiceXP.VoiceLevel;
            int requiredXP = userVoiceXP.VoiceRequiredXp;
            int level = initialLevel;

            // Level up logic
            while (userVoiceXP.VoiceXp >= requiredXP)
            {
                userVoiceXP.VoiceXp -= requiredXP;
                level++;
                requiredXP = 150 + (level - 1) * 150;
                _logger.LogInformation($"User {user.Username} ({user.Id}) leveled up to voice level {level}");

                // Handle role changes for level up
                await UpdateUserRoleForLevel(level, user, levelRoleMappings);
            }

            userVoiceXP.VoiceLevel = level;
            userVoiceXP.VoiceRequiredXp = requiredXP;
            await _databaseManager.SaveUserVoiceXP(userVoiceXP);

            // Send level-up message if user leveled up
            if (level > initialLevel)
            {
                await SendLevelUpMessage(user, level);
            }
        }

        /// <summary>
        /// Updates the user's role based on their new voice level
        /// </summary>
        private async Task UpdateUserRoleForLevel(int level, DiscordUser user, List<LevelToRoleVoice> levelRoleMappings)
        {
            try
            {
                var roleMapping = levelRoleMappings.FirstOrDefault(mapping => mapping.Level == level);
                if (roleMapping != null)
                {
                    ulong roleId = (ulong)roleMapping.RoleId;

                    // Remove all level roles
                    foreach (var mapping in levelRoleMappings)
                    {
                        var roleToRemove = await _levelUpChannel.Guild.GetRoleAsync((ulong)mapping.RoleId);
                        if (roleToRemove != null)
                        {
                            var member = await _levelUpChannel.Guild.GetMemberAsync(user.Id);
                            if (member != null && member.Roles.Contains(roleToRemove))
                            {
                                await member.RevokeRoleAsync(roleToRemove);
                                _logger.LogDebug($"Removed voice role {roleToRemove.Name} from user {user.Username} ({user.Id})");
                            }
                        }
                    }

                    // Grant the new level role
                    var role = await _levelUpChannel.Guild.GetRoleAsync(roleId);
                    if (role != null)
                    {
                        var member = await _levelUpChannel.Guild.GetMemberAsync(user.Id);
                        if (member != null)
                        {
                            await member.GrantRoleAsync(role);
                            _logger.LogInformation($"Granted voice level {level} role {role.Name} to user {user.Username} ({user.Id})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating user role for voice level {level}, user {user.Id}");
            }
        }

        /// <summary>
        /// Sends a level up message to the configured channel
        /// </summary>
        private async Task SendLevelUpMessage(DiscordUser user, int level)
        {
            if (_levelUpChannel == null)
                return;

            try
            {
                // Sometimes send a kawaii message for fun
                double randomValue = _random.NextDouble();
                if (randomValue <= CRINGE_MESSAGE_CHANCE)
                {
                    await SendKawaiiLevelUpEmbed(user, level);
                }
                else
                {
                    await SendStandardLevelUpEmbed(user, level);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending voice level up message for user {user.Id}");
            }
        }

        /// <summary>
        /// Sends a standard level up message
        /// </summary>
        private async Task SendStandardLevelUpEmbed(DiscordUser user, int level)
        {
            var embed = new DiscordEmbedBuilder()
                .WithTitle("Voice Level Up!")
                .WithDescription($"{user.Mention} has reached level {level} in voice activity!")
                .WithColor(DiscordColor.Purple)
                .WithThumbnail(user.AvatarUrl)
                .WithFooter("Keep those conversations going!");

            await _levelUpChannel.SendMessageAsync(embed: embed);
        }

        /// <summary>
        /// Sends a cute kawaii level up message (rare)
        /// </summary>
        private async Task SendKawaiiLevelUpEmbed(DiscordUser user, int level)
        {
            var embed = new DiscordEmbedBuilder()
                .WithTitle("✨ Voice Level Up! ✨")
                .WithDescription($"Hey, senpai~! {user.Mention}-chan has ascended to level {level} in voice activity!~ ❤️✨")
                .WithColor(DiscordColor.HotPink)
                .WithThumbnail(user.AvatarUrl)
                .WithFooter("Your voice is so amazing! (≧◡≦)");

            await _levelUpChannel.SendMessageAsync(embed: embed);
        }

        /// <summary>
        /// Gets the current tax rate based on bank balance
        /// </summary>
        public async Task<double> GetTax()
        {
            try
            {
                int bank = await _databaseManager.GetTax();
                double tax;

                if (bank < 50000)
                {
                    tax = 0.15;
                }
                else if (bank < 100000)
                {
                    tax = 0.1;
                }
                else if (bank < 200000)
                {
                    tax = 0.05;
                }
                else
                {
                    tax = 0.02;
                }

                _logger.LogDebug($"Current voice tax rate: {tax:P}, Bank balance: {bank}");
                return tax;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating voice tax rate");
                return 0.15; // Default to highest rate on error
            }
        }

        /// <summary>
        /// Checks if a user has enough voice XP for a deposit
        /// </summary>
        public async Task<bool> CheckUserDeposit(ulong userId, long amount)
        {
            try
            {
                var user = await _databaseManager.GetUserVoiceXP(userId);
                if (user == null || user.VoiceXp < amount)
                {
                    _logger.LogDebug($"User {userId} doesn't have enough voice XP for deposit of {amount}");
                    return true; // Insufficient funds
                }

                return false; // Has sufficient funds
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking user voice deposit for user {userId}");
                return true; // Err on the side of caution
            }
        }

        /// <summary>
        /// Deposits voice XP to user's stored XP
        /// </summary>
        public async Task Deposit(ulong userId, long amount)
        {
            try
            {
                var user = await _databaseManager.GetUserVoiceXP(userId);

                var taxDeduction = (int)(amount * 0.1);
                await _databaseManager.SaveTax(taxDeduction);

                _logger.LogInformation($"User {userId} deposited {amount} voice XP (tax: {taxDeduction})");

                user.VoiceXp -= (int)amount;
                amount -= taxDeduction;

                user.StoredVoiceXp += (int)amount;

                await _databaseManager.SaveUserVoiceXP(user);
                await _databaseManager.SaveTax((int)amount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing voice deposit for user {userId}");
            }
        }

        /// <summary>
        /// Withdraws all stored voice XP back to user's active XP
        /// </summary>
        public async Task<int> Withdraw(ulong userId, DiscordUser discordUser)
        {
            try
            {
                var user = await _databaseManager.GetUserVoiceXP(userId);

                int amount = user.StoredVoiceXp;
                user.VoiceXp += user.StoredVoiceXp;
                user.StoredVoiceXp = 0;

                await _databaseManager.WithdrawTax(amount);

                _logger.LogInformation($"User {discordUser.Username} ({userId}) withdrew {amount} voice XP");

                await CalculateLevelAndRequiredXP(user, discordUser);

                return amount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing voice withdrawal for user {userId}");
                return 0;
            }
        }

        /// <summary>
        /// Gets a user's stored voice XP balance
        /// </summary>
        public async Task<int> VoiceBalance(ulong userId)
        {
            try
            {
                var user = await _databaseManager.GetUserVoiceXP(userId);
                return user?.StoredVoiceXp ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting voice balance for user {userId}");
                return 0;
            }
        }

        /// <summary>
        /// Checks if users have enough voice XP for a steal attempt
        /// </summary>
        public async Task<bool> CheckUserXP(ulong thiefId, ulong targetId, long amount)
        {
            try
            {
                var thief = await _databaseManager.GetUserVoiceXP(thiefId);
                var target = await _databaseManager.GetUserVoiceXP(targetId);

                if (thief == null || thief.VoiceXp < amount / 2 || target == null || target.VoiceXp < amount)
                {
                    _logger.LogDebug($"Insufficient voice XP for steal: thief {thiefId} ({thief?.VoiceXp ?? 0}), target {targetId} ({target?.VoiceXp ?? 0}), amount {amount}");
                    return true; // Insufficient funds
                }

                return false; // Has sufficient funds
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking voice XP for steal attempt");
                return true; // Err on the side of caution
            }
        }

        /// <summary>
        /// Checks if a user has enough voice XP for a heist
        /// </summary>
        public async Task<bool> CheckHeistXP(ulong userId)
        {
            try
            {
                var user = await _databaseManager.GetUserVoiceXP(userId);
                if (user == null || user.VoiceXp < 500)
                {
                    return true; // Insufficient funds
                }
                return false; // Has sufficient funds
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking heist voice XP for user {userId}");
                return true; // Err on the side of caution
            }
        }

        /// <summary>
        /// Checks if a user has enough voice XP to donate
        /// </summary>
        public async Task<bool> CheckDonateXP(ulong userId)
        {
            try
            {
                var user = await _databaseManager.GetUserVoiceXP(userId);
                if (user == null || user.VoiceXp < 1500)
                {
                    return true; // Insufficient funds
                }
                return false; // Has sufficient funds
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking donate voice XP for user {userId}");
                return true; // Err on the side of caution
            }
        }

        /// <summary>
        /// Processes a donation and returns the karma increase
        /// </summary>
        public async Task<double> Donate(ulong userId)
        {
            try
            {
                var user = await _databaseManager.GetUserVoiceXP(userId);
                var bank = await _databaseManager.GetUserVoiceXP(TAX_BANK_USER_ID);

                user.VoiceXp -= 1500;
                bank.VoiceXp += 1500;

                double karmaIncrease;

                if (user.Karma < 0.3)
                {
                    // Lower karma users get larger boosts (up to 0.6)
                    karmaIncrease = 0.6 - (0.8 * user.Karma);
                }
                else if (user.Karma < 0.7)
                {
                    // Mid-range users get moderate boosts
                    // This creates a smoother transition from 0.3 to 0.7
                    karmaIncrease = 0.45 - (0.4 * user.Karma);
                }
                else if (user.Karma < 1.0)
                {
                    // Higher karma users get smaller but still useful boosts
                    karmaIncrease = 0.2 - (0.15 * user.Karma);
                }
                else
                {
                    // Players at or above 1.0 get a small standard boost
                    karmaIncrease = 0.03;
                }

                user.Karma += karmaIncrease;
                if (user.Karma > 1.3)
                {
                    user.Karma = 1.3;
                    karmaIncrease = 0;
                }

                _logger.LogInformation($"User {userId} donated 1500 voice XP and gained {karmaIncrease:F2} karma (new karma: {user.Karma:F2})");

                await _databaseManager.SaveUserVoiceXP(user);
                await _databaseManager.SaveUserVoiceXP(bank);

                return karmaIncrease;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing voice donation for user {userId}");
                return 0;
            }
        }

        /// <summary>
        /// Attempts to steal voice XP from another user
        /// </summary>
        public async Task<double> StealXP(long amount, DiscordUser thiefU, DiscordUser targetU, bool isProtected, DiscordUser protectU)
        {
            try
            {
                var thief = await _databaseManager.GetUserVoiceXP(thiefU.Id);
                var target = await _databaseManager.GetUserVoiceXP(targetU.Id);
                var successRate = _random.NextDouble();

                double threshold = thief.Karma switch
                {
                    < 0.2 => 0.7,
                    < 0.4 => 0.65,
                    < 0.55 => 0.6,
                    < 0.7 => 0.55,
                    _ => 0.5
                };

                if (isProtected) threshold = 0.2;

                bool stealSuccess = successRate <= threshold;
                _logger.LogInformation($"Voice XP steal attempt: {thiefU.Username} trying to steal {amount} XP from {targetU.Username}. " +
                                    $"Success rate: {successRate:F2}, Threshold: {threshold:F2}, Protected: {isProtected}, Success: {stealSuccess}");

                if (stealSuccess)
                {
                    thief.VoiceXp += (int)amount;
                    target.VoiceXp -= (int)amount;

                    await _databaseManager.SaveUserVoiceXP(target);
                    await CalculateLevelAndRequiredXP(thief, thiefU);
                    return 0;
                }
                else
                {
                    if (isProtected == false)
                    {
                        _logger.LogDebug($"Failed voice steal: {thiefU.Username} lost 0.08 karma, new karma: {thief.Karma:F2}");

                        var karmaReduction = 0.05;
                        karmaReduction = CalculateKarma(thief, karmaReduction);

                        await _databaseManager.SaveUserVoiceXP(thief);
                        return karmaReduction;
                    }
                    else
                    {
                        var karmaReduction = 0.1;
                        karmaReduction = CalculateKarma(thief, karmaReduction);

                        var protector = await _databaseManager.GetUserVoiceXP(protectU.Id);

                        thief.VoiceXp -= (int)amount / 2;
                        target.VoiceXp += (int)amount / 2;

                        protector.Karma += 0.1;

                        if (protector.Karma > 1.3)
                        {
                            protector.Karma = 1.3;
                        }

                        _logger.LogDebug($"Protected voice steal failure: {thiefU.Username} lost {amount / 2} XP and 0.05 karma. " +
                                       $"Protector {protectU.Username} gained 0.1 karma (now {protector.Karma:F2})");

                        await _databaseManager.SaveUserVoiceXP(thief);
                        await _databaseManager.SaveUserVoiceXP(protector);
                        await CalculateLevelAndRequiredXP(target, targetU);

                        return karmaReduction;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing voice steal attempt from {thiefU.Id} to {targetU.Id}");
                return -1;
            }
        }

        /// <summary>
        /// Attempts a heist on the bank for voice XP
        /// </summary>
        public async Task<(int, double)> Heist(DiscordUser user)
        {
            try
            {
                var thief = await _databaseManager.GetUserVoiceXP(user.Id);
                var bank = await _databaseManager.GetUserVoiceXP(TAX_BANK_USER_ID);
                var successRate = _random.NextDouble();
                var threshold = 0.01;

                if (thief.Karma < 0.4)
                {
                    threshold = 0.015;
                }
                if (thief.Karma < 0.2)
                {
                    threshold = 0.02;
                }

                bool heistSuccess = successRate <= threshold;
                _logger.LogInformation($"Voice heist attempt by {user.Username}: Rate {successRate:F4}, Threshold {threshold:F4}, Success: {heistSuccess}");

                if (heistSuccess)
                {
                    var prize = bank.VoiceXp;
                    thief.VoiceXp += bank.VoiceXp;
                    bank.VoiceXp = 0;

                    await _databaseManager.WipeBankData();
                    await _databaseManager.SaveUserVoiceXP(bank);
                    await CalculateLevelAndRequiredXP(thief, user);

                    _logger.LogInformation($"Successful voice heist by {user.Username}! Stole {prize} XP from bank.");
                    return (prize, 0);
                }
                else
                {
                    thief.VoiceXp -= 500;
                    bank.VoiceXp += 500;
                    double lostKarma = 0;
                    if (thief.Karma >= 0.01)
                    {
                        lostKarma = thief.Karma / 2;
                        thief.Karma -= lostKarma;
                    }

                    _logger.LogDebug($"Failed voice heist by {user.Username}. Lost 500 XP and karma reduced to {lostKarma:F2}");

                    await _databaseManager.SaveUserVoiceXP(thief);
                    await _databaseManager.SaveUserVoiceXP(bank);

                    return (0, lostKarma);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing voice heist for user {user.Id}");
                return (-1, -1);
            }
        }


        private double CalculateKarma(UserData thief, double karmaReduction)
        {
            var initialKarma = thief.Karma;

            thief.Karma -= karmaReduction;

            // Ensure karma does not drop below 0.01
            if (thief.Karma < 0.01)
            {
                karmaReduction = initialKarma - 0.01; // Adjust karma reduction to match the actual decrease
                thief.Karma = 0.01;
            }

            return karmaReduction;
        }
    }
}