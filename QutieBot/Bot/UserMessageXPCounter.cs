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
    /// Manages user experience points for message activity
    /// </summary>
    public class UserMessageXPCounter
    {
        private readonly UserMessageXPCounterDAL _databaseManager;
        private readonly ILogger<UserMessageXPCounter> _logger;
        private DiscordChannel _levelUpChannel;
        private readonly Random _random = new Random();
        private readonly Dictionary<ulong, DateTime> _userCooldowns = new Dictionary<ulong, DateTime>();

        // Constants for Discord IDs and other configuration
        private const ulong LEVEL_UP_CHANNEL_ID = 1151618049250709645;
        private const ulong LEADERSHIP_ROLE_ID = 1152617541190041600;
        private const ulong TAX_BANK_USER_ID = 1158671215146315796;
        private const double CRINGE_MESSAGE_CHANCE = 0.01;

        /// <summary>
        /// Initializes a new instance of the UserMessageXPCounter class
        /// </summary>
        /// <param name="databaseManager">The database manager for user XP</param>
        /// <param name="logger">The logger instance</param>
        public UserMessageXPCounter(
            UserMessageXPCounterDAL databaseManager,
            ILogger<UserMessageXPCounter> logger)
        {
            _databaseManager = databaseManager ?? throw new ArgumentNullException(nameof(databaseManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Handles message creation events to award XP to users
        /// </summary>
        /// <param name="client">The Discord client that raised the event</param>
        /// <param name="e">Event arguments containing message information</param>
        public async Task OnMessageReceived(DiscordClient client, MessageCreatedEventArgs e)
        {
            try
            {
                // Skip messages from bots
                if (e.Author.IsBot)
                    return;

                _levelUpChannel = await client.GetChannelAsync(LEVEL_UP_CHANNEL_ID);

                var config = await _databaseManager.GetMessageConfig();
                _logger.LogDebug($"Retrieved message XP config. Min XP: {config.MessageMinXp}, Max XP: {config.MessageMaxXp}, Cooldown: {config.MessageCooldown}s");

                var userMessageXP = await _databaseManager.GetUserMessageXP(e.Author.Id);
                userMessageXP.MessageCount++;
                int xpEarned = 0;   

                // Check if user is on cooldown
                if (!_userCooldowns.ContainsKey(e.Author.Id) ||
                    DateTime.Now - _userCooldowns[e.Author.Id] >= TimeSpan.FromSeconds(config.MessageCooldown))
                {
                    xpEarned = await ProcessXPEarning(e, config, userMessageXP);
                }

                await _databaseManager.UpdateUserMessageActivity(e.Author.Id, xpEarned);

                await CalculateLevelAndRequiredXP(userMessageXP, e.Author);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing message XP for user {e.Author.Id}");
            }
        }

        /// <summary>
        /// Processes XP earning for a user message
        /// </summary>
        private async Task<int> ProcessXPEarning(MessageCreatedEventArgs e, Xpconfig config, UserData userMessageXP)
        {
            var xpEarned = _random.Next(config.MessageMinXp, config.MessageMaxXp + 1);
            _logger.LogDebug($"User {e.Author.Username} ({e.Author.Id}) earned {xpEarned} raw XP");

            var guild = e.Guild;
            var guildMember = await guild.GetMemberAsync(e.Author.Id);
            bool isLeadership = guildMember.Roles.Any(role => role.Id == LEADERSHIP_ROLE_ID);

            // Apply tax if not leadership
            double tax = await GetTax();
            var taxDeduction = (int)(xpEarned * tax);

            if (!isLeadership)
            {
                _logger.LogDebug($"Applying tax rate of {tax:P} to user {e.Author.Id}. Tax: {taxDeduction} XP");
                xpEarned -= taxDeduction;
            }

            await _databaseManager.SaveTax(taxDeduction);

            // Update karma if needed
            if (userMessageXP.Karma < 1)
            {
                double baseKarmaIncrease = xpEarned * 0.0001;
                double karmaFactor = 0.1 + (0.9 * userMessageXP.Karma);
                double karmaIncrease = baseKarmaIncrease * karmaFactor;
                userMessageXP.Karma += karmaIncrease;

                if (userMessageXP.Karma > 1)
                {
                    userMessageXP.Karma = 1;
                }

                _logger.LogDebug($"User {e.Author.Id} karma increased by {karmaIncrease:F4} to {userMessageXP.Karma:F4}");
            }

            // Apply karma modifier to XP
            xpEarned = (int)(xpEarned * userMessageXP.Karma);
            userMessageXP.MessageXp += xpEarned;

            _logger.LogInformation($"User {e.Author.Username} ({e.Author.Id}) earned {xpEarned} XP (after karma modifier: {userMessageXP.Karma:F2})");

            // Update cooldown timer
            _userCooldowns[e.Author.Id] = DateTime.Now;

            return xpEarned;
        }

        /// <summary>
        /// Calculates user level based on XP and handles level-up events
        /// </summary>
        private async Task CalculateLevelAndRequiredXP(UserData userMessageXP, DiscordUser user)
        {
            List<LevelToRoleMessage> levelRoleMappings = await _databaseManager.GetLevelRoleMessages();
            int initialLevel = userMessageXP.MessageLevel;
            int requiredXP = userMessageXP.MessageRequiredXp;
            int level = initialLevel;

            // Level up logic
            while (userMessageXP.MessageXp >= requiredXP)
            {
                userMessageXP.MessageXp -= requiredXP;
                level++;
                requiredXP = 150 + (level - 1) * 150;
                _logger.LogInformation($"User {user.Username} ({user.Id}) leveled up to level {level}");

                // Handle role changes for level up
                await UpdateUserRoleForLevel(level, user, levelRoleMappings);
            }

            userMessageXP.MessageLevel = level;
            userMessageXP.MessageRequiredXp = requiredXP;
            await _databaseManager.SaveUserMessageXP(userMessageXP);

            // Send level-up message if user leveled up
            if (level > initialLevel)
            {
                await SendLevelUpMessage(user, level);
            }
        }

        /// <summary>
        /// Updates the user's role based on their new level
        /// </summary>
        private async Task UpdateUserRoleForLevel(int level, DiscordUser user, List<LevelToRoleMessage> levelRoleMappings)
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
                                _logger.LogDebug($"Removed role {roleToRemove.Name} from user {user.Username} ({user.Id})");
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
                            _logger.LogInformation($"Granted level {level} role {role.Name} to user {user.Username} ({user.Id})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating user role for level {level}, user {user.Id}");
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
                _logger.LogError(ex, $"Error sending level up message for user {user.Id}");
            }
        }

        /// <summary>
        /// Sends a standard level up message
        /// </summary>
        private async Task SendStandardLevelUpEmbed(DiscordUser user, int level)
        {
            var embed = new DiscordEmbedBuilder()
                .WithTitle("Message Level Up!")
                .WithDescription($"{user.Mention} has reached level {level} in message activity!")
                .WithColor(DiscordColor.Green)
                .WithThumbnail(user.AvatarUrl)
                .WithFooter("Keep up the great conversations!");

            await _levelUpChannel.SendMessageAsync(embed: embed);
        }

        /// <summary>
        /// Sends a cute kawaii level up message (rare)
        /// </summary>
        private async Task SendKawaiiLevelUpEmbed(DiscordUser user, int level)
        {
            var embed = new DiscordEmbedBuilder()
                .WithTitle("✨ Message Level Up! ✨")
                .WithDescription($"Hey, senpai~! {user.Mention}-chan has ascended to level {level} in message activity!~ ❤️✨")
                .WithColor(DiscordColor.HotPink)
                .WithThumbnail(user.AvatarUrl)
                .WithFooter("S-so proud of you! ヽ(・∀・)ﾉ");

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

                _logger.LogDebug($"Current tax rate: {tax:P}, Bank balance: {bank}");
                return tax;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating tax rate");
                return 0.15; // Default to highest rate on error
            }
        }

        /// <summary>
        /// Checks if a user has enough XP for a deposit
        /// </summary>
        public async Task<bool> CheckUserDeposit(ulong userId, long amount)
        {
            try
            {
                var user = await _databaseManager.GetUserMessageXP(userId);
                if (user == null || user.MessageXp < amount)
                {
                    _logger.LogDebug($"User {userId} doesn't have enough XP for deposit of {amount}");
                    return true; // Insufficient funds
                }

                return false; // Has sufficient funds
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking user deposit for user {userId}");
                return true; // Err on the side of caution
            }
        }

        /// <summary>
        /// Deposits XP to user's stored XP
        /// </summary>
        public async void Deposit(ulong userId, long amount)
        {
            try
            {
                var user = await _databaseManager.GetUserMessageXP(userId);

                var taxDeduction = (int)(amount * 0.1);
                await _databaseManager.SaveTax(taxDeduction);

                _logger.LogInformation($"User {userId} deposited {amount} XP (tax: {taxDeduction})");

                user.MessageXp -= (int)amount;
                amount -= taxDeduction;

                user.StoredMessageXp += (int)amount;

                await _databaseManager.SaveUserMessageXP(user);
                await _databaseManager.SaveTax((int)amount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing deposit for user {userId}");
            }
        }

        /// <summary>
        /// Withdraws all stored XP back to user's active XP
        /// </summary>
        public async Task<int> Withdraw(ulong userId, DiscordUser discordUser)
        {
            try
            {
                var user = await _databaseManager.GetUserMessageXP(userId);

                int amount = user.StoredMessageXp;
                user.MessageXp += user.StoredMessageXp;
                user.StoredMessageXp = 0;

                _databaseManager.WithdrawTax(amount);

                _logger.LogInformation($"User {discordUser.Username} ({userId}) withdrew {amount} XP");

                await CalculateLevelAndRequiredXP(user, discordUser);

                return amount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing withdrawal for user {userId}");
                return 0;
            }
        }

        /// <summary>
        /// Gets a user's stored XP balance
        /// </summary>
        public async Task<int> MessageBalance(ulong userId)
        {
            try
            {
                var user = await _databaseManager.GetUserMessageXP(userId);
                return user?.StoredMessageXp ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting message balance for user {userId}");
                return 0;
            }
        }

        /// <summary>
        /// Checks if users have enough XP for a steal attempt
        /// </summary>
        public async Task<bool> CheckUserXP(ulong thiefId, ulong targetId, long amount)
        {
            try
            {
                var thief = await _databaseManager.GetUserMessageXP(thiefId);
                var target = await _databaseManager.GetUserMessageXP(targetId);

                if (thief == null || thief.MessageXp < amount / 2 || target == null || target.MessageXp < amount)
                {
                    _logger.LogDebug($"Insufficient XP for steal: thief {thiefId} ({thief?.MessageXp ?? 0}), target {targetId} ({target?.MessageXp ?? 0}), amount {amount}");
                    return true; // Insufficient funds
                }

                return false; // Has sufficient funds
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking XP for steal attempt");
                return true; // Err on the side of caution
            }
        }

        /// <summary>
        /// Checks if a user has enough XP for a heist
        /// </summary>
        public async Task<bool> CheckHeistXP(ulong userId)
        {
            try
            {
                var user = await _databaseManager.GetUserMessageXP(userId);
                if (user == null || user.MessageXp < 500)
                {
                    return true; // Insufficient funds
                }
                return false; // Has sufficient funds
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking heist XP for user {userId}");
                return true; // Err on the side of caution
            }
        }

        /// <summary>
        /// Checks if a user has enough XP to donate
        /// </summary>
        public async Task<bool> CheckDonateXP(ulong userId)
        {
            try
            {
                var user = await _databaseManager.GetUserMessageXP(userId);
                if (user == null || user.MessageXp < 1500)
                {
                    return true; // Insufficient funds
                }
                return false; // Has sufficient funds
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking donate XP for user {userId}");
                return true; // Err on the side of caution
            }
        }

        /// <summary>
        /// Processes a donation and returns the karma increase
        /// </summary>
        public async Task<double> Donate(ulong userId)
        {
            var user = await _databaseManager.GetUserMessageXP(userId);
            var bank = await _databaseManager.GetUserMessageXP(TAX_BANK_USER_ID);
            user.MessageXp -= 1500;
            bank.MessageXp += 1500;
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
            }

            await _databaseManager.SaveUserMessageXP(user);
            await _databaseManager.SaveUserMessageXP(bank);
            return karmaIncrease;
        }

        /// <summary>
        /// Attempts to steal XP from another user
        /// </summary>
        public async Task<double> StealXP(long amount, DiscordUser thiefU, DiscordUser targetU, bool isProtected, DiscordUser protectU)
        {
            try
            {
                var thief = await _databaseManager.GetUserMessageXP(thiefU.Id);
                var target = await _databaseManager.GetUserMessageXP(targetU.Id);
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
                _logger.LogInformation($"Steal attempt: {thiefU.Username} trying to steal {amount} XP from {targetU.Username}. " +
                                    $"Success rate: {successRate:F2}, Threshold: {threshold:F2}, Protected: {isProtected}, Success: {stealSuccess}");

                if (stealSuccess)
                {
                    thief.MessageXp += (int)amount;
                    target.MessageXp -= (int)amount;

                    await _databaseManager.SaveUserMessageXP(target);
                    await CalculateLevelAndRequiredXP(thief, thiefU);
                    return 0;
                }
                else
                {
                    if (isProtected == false)
                    {
                        _logger.LogDebug($"Failed steal: {thiefU.Username} lost 0.05 karma, new karma: {thief.Karma:F2}");

                        var karmaReduction = 0.05;
                        karmaReduction = CalculateKarma(thief, karmaReduction);

                        await _databaseManager.SaveUserMessageXP(thief);
                        return karmaReduction;
                    }
                    else
                    {

                        var karmaReduction = 0.1;
                        karmaReduction = CalculateKarma(thief, karmaReduction);

                        var protector = await _databaseManager.GetUserMessageXP(protectU.Id);

                        thief.MessageXp -= (int)amount / 2;
                        target.MessageXp += (int)amount / 2;

                        protector.Karma += 0.1;

                        if (protector.Karma > 1.3)
                        {
                            protector.Karma = 1.3;
                        }

                        _logger.LogDebug($"Protected steal failure: {thiefU.Username} lost {amount / 2} XP and 0.05 karma. " +
                                        $"Protector {protectU.Username} gained 0.1 karma (now {protector.Karma:F2})");

                        await _databaseManager.SaveUserMessageXP(thief);
                        await _databaseManager.SaveUserMessageXP(protector);
                        await CalculateLevelAndRequiredXP(target, targetU);

                        return karmaReduction;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing steal attempt from {thiefU.Id} to {targetU.Id}");
                return -1;
            }
        }

        /// <summary>
        /// Attempts a heist on the bank
        /// </summary>
        public async Task<(int, double)> Heist(DiscordUser user)
        {
            try
            {
                var thief = await _databaseManager.GetUserMessageXP(user.Id);
                var bank = await _databaseManager.GetUserMessageXP(TAX_BANK_USER_ID);
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
                _logger.LogInformation($"Heist attempt by {user.Username}: Rate {successRate:F4}, Threshold {threshold:F4}, Success: {heistSuccess}");

                if (heistSuccess)
                {
                    var prize = bank.MessageXp;
                    thief.MessageXp += bank.MessageXp;
                    bank.MessageXp = 0;

                    await _databaseManager.WipeBankData();
                    await _databaseManager.SaveUserMessageXP(bank);
                    await CalculateLevelAndRequiredXP(thief, user);

                    _logger.LogInformation($"Successful heist by {user.Username}! Stole {prize} XP from bank.");
                    return (prize, 0);
                }
                else
                {
                    thief.MessageXp -= 500;
                    bank.MessageXp += 500;

                    double lostKarma = 0;
                    if (thief.Karma >= 0.01)
                    {
                        lostKarma = thief.Karma / 2;
                        thief.Karma -= lostKarma;
                    }

                    _logger.LogDebug($"Failed heist by {user.Username}. Lost 500 XP and karma reduced to {lostKarma:F2}");

                    await _databaseManager.SaveUserMessageXP(thief);
                    await _databaseManager.SaveUserMessageXP(bank);

                    return (0, lostKarma);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing heist for user {user.Id}");
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