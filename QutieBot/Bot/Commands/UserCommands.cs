using DSharpPlus.Entities;
using DSharpPlus;
using DSharpPlus.Commands;
using System.Text;
using System;
using System.ComponentModel;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using Microsoft.Extensions.Logging;

namespace QutieBot.Bot.Commands
{
    public class UserCommands
    {
        private readonly GenerateImage _generateImage;
        private readonly UserMessageXPCounter _userMessageXPCounter;
        private readonly UserVoiceXPCounter _userVoiceXPCounter;
        private readonly ILogger<UserCommands> _logger;
        private readonly Random _random = new Random();

        // Static collections for tracking cooldowns and protections
        private static readonly Dictionary<ulong, DateTime> _actionCooldowns = new Dictionary<ulong, DateTime>();
        private static readonly Dictionary<ulong, DateTime> _bankCooldowns = new Dictionary<ulong, DateTime>();
        private static readonly Dictionary<ulong, DateTime> _specialActionCooldowns = new Dictionary<ulong, DateTime>();
        private static readonly Dictionary<ulong, ProtectionInfo> _protections = new Dictionary<ulong, ProtectionInfo>();

        // Bot's user ID for checks
        private const ulong BotUserId = 1158671215146315796;

        public UserCommands(
            GenerateImage generateImage,
            UserVoiceXPCounter userVoiceXPCounter,
            UserMessageXPCounter userMessageXPCounter,
            ILogger<UserCommands> logger)
        {
            _generateImage = generateImage ?? throw new ArgumentNullException(nameof(generateImage));
            _userMessageXPCounter = userMessageXPCounter ?? throw new ArgumentNullException(nameof(userMessageXPCounter));
            _userVoiceXPCounter = userVoiceXPCounter ?? throw new ArgumentNullException(nameof(userVoiceXPCounter));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Command("help"), Description("Show all available user commands")]
        public async Task ShowCommandList(CommandContext ctx)
        {
            _logger.LogInformation($"User {ctx.User.Id} requested command list");

            var commandsList = new StringBuilder();

            // Level and XP Commands
            commandsList.AppendLine("📊 **Level & XP Commands**");
            commandsList.AppendLine("`/rank` - View your or another member's level and XP progress");
            commandsList.AppendLine("`/voice_leaderboard` - View top users by voice activity");
            commandsList.AppendLine("`/message_leaderboard` - View top users by message activity");
            commandsList.AppendLine("`/leaderboard` - View top users by combined rank");
            commandsList.AppendLine();

            // Economy Commands
            commandsList.AppendLine("💰 **Economy Commands**");
            commandsList.AppendLine("`/deposit` - Store XP in the bank (10% tax applied)");
            commandsList.AppendLine("`/withdraw` - Withdraw your saved XP");
            commandsList.AppendLine("`/balance` - Check your bank balance");
            commandsList.AppendLine("`/cooldowns` - Check your action cooldowns");
            commandsList.AppendLine();

            // Action Commands
            commandsList.AppendLine("🎯 **Action Commands**");
            commandsList.AppendLine("`/donate` - Donate XP to raise your Karma");
            commandsList.AppendLine("`/heist` - Attempt a bank heist for XP");
            commandsList.AppendLine("`/guard` - Protect a user from theft");
            commandsList.AppendLine("`/steal` - Steal XP from another user");

            var embed = new DiscordEmbedBuilder()
                .WithTitle("Available Commands")
                .WithDescription(commandsList.ToString())
                .WithColor(DiscordColor.Blurple);

            await ctx.RespondAsync(embed.Build());
        }

        [Command("rank"), Description("View your level and XP progress")]
        public async Task ShowUserRank(CommandContext context, [Description("User to view (leave empty for yourself)")] DiscordMember? user = null)
        {
            await context.DeferResponseAsync();

            try
            {
                ulong userId = user?.Id ?? context.User.Id;
                _logger.LogInformation($"Generating rank image for user {userId}, requested by {context.User.Id}");

                byte[]? imageData = await _generateImage.GenerateUserImage((long)userId);

                if (imageData == null || imageData.Length == 0)
                {
                    _logger.LogWarning($"Failed to generate rank image for user {userId}");
                    await context.EditResponseAsync(new DiscordWebhookBuilder()
                        .WithContent("Failed to generate rank image. Please try again later."));
                    return;
                }

                using (MemoryStream imageStream = new MemoryStream(imageData))
                {
                    var builder = new DiscordWebhookBuilder();
                    builder.AddFile("rank_card.png", imageStream);
                    await context.EditResponseAsync(builder);
                }

                _logger.LogInformation($"Successfully generated rank image for user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating rank image for {user?.Id ?? context.User.Id}");
                await context.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent("An error occurred while generating your rank card. Please try again later."));
            }
        }

        [Command("voice_leaderboard"), Description("View top users by voice activity")]
        public async Task ShowVoiceLeaderboard(CommandContext context)
        {
            await context.DeferResponseAsync();

            try
            {
                _logger.LogInformation($"Generating voice leaderboard, requested by {context.User.Id}");

                byte[] imageData = await _generateImage.GenerateVoiceRank();

                if (imageData == null || imageData.Length == 0)
                {
                    _logger.LogWarning("Failed to generate voice leaderboard image");
                    await context.EditResponseAsync(new DiscordWebhookBuilder()
                        .WithContent("Failed to generate voice leaderboard. Please try again later."));
                    return;
                }

                using (MemoryStream imageStream = new MemoryStream(imageData))
                {
                    var builder = new DiscordWebhookBuilder();
                    builder.AddFile("voice_leaderboard.png", imageStream);
                    await context.EditResponseAsync(builder);
                }

                _logger.LogInformation("Successfully generated voice leaderboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating voice leaderboard");
                await context.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent("An error occurred while generating the voice leaderboard. Please try again later."));
            }
        }

        [Command("message_leaderboard"), Description("View top users by message activity")]
        public async Task ShowMessageLeaderboard(CommandContext context)
        {
            await context.DeferResponseAsync();

            try
            {
                _logger.LogInformation($"Generating message leaderboard, requested by {context.User.Id}");

                byte[] imageData = await _generateImage.GenerateMessageRank();

                if (imageData == null || imageData.Length == 0)
                {
                    _logger.LogWarning("Failed to generate message leaderboard image");
                    await context.EditResponseAsync(new DiscordWebhookBuilder()
                        .WithContent("Failed to generate message leaderboard. Please try again later."));
                    return;
                }

                using (MemoryStream imageStream = new MemoryStream(imageData))
                {
                    var builder = new DiscordWebhookBuilder();
                    builder.AddFile("message_leaderboard.png", imageStream);
                    await context.EditResponseAsync(builder);
                }

                _logger.LogInformation("Successfully generated message leaderboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating message leaderboard");
                await context.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent("An error occurred while generating the message leaderboard. Please try again later."));
            }
        }

        [Command("leaderboard"), Description("View top users by combined rank")]
        public async Task ShowCombinedLeaderboard(CommandContext context)
        {
            await context.DeferResponseAsync();

            try
            {
                _logger.LogInformation($"Generating combined leaderboard, requested by {context.User.Id}");

                byte[] imageData = await _generateImage.GenerateLeaderboard();

                if (imageData == null || imageData.Length == 0)
                {
                    _logger.LogWarning("Failed to generate combined leaderboard image");
                    await context.EditResponseAsync(new DiscordWebhookBuilder()
                        .WithContent("Failed to generate combined leaderboard. Please try again later."));
                    return;
                }

                using (MemoryStream imageStream = new MemoryStream(imageData))
                {
                    var builder = new DiscordWebhookBuilder();
                    builder.AddFile("leaderboard.png", imageStream);
                    await context.EditResponseAsync(builder);
                }

                _logger.LogInformation("Successfully generated combined leaderboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating combined leaderboard");
                await context.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent("An error occurred while generating the leaderboard. Please try again later."));
            }
        }

        [Command("deposit"), Description("Store XP in the bank (10% tax applied)")]
        public async Task DepositXP(
            CommandContext ctx,
            [Description("Type of XP to deposit"), SlashChoiceProvider<XpTypeProvider>] string type,
            [Description("Amount of XP to deposit")] long amount)
        {
            try
            {
                _logger.LogInformation($"User {ctx.User.Id} attempting to deposit {amount} {type} XP");

                // Check if user is on bank cooldown
                if (_bankCooldowns.TryGetValue(ctx.User.Id, out DateTime lastBankTime))
                {
                    TimeSpan cooldown = DateTime.UtcNow - lastBankTime;
                    if (cooldown.TotalHours < 24)
                    {
                        TimeSpan remaining = TimeSpan.FromHours(24) - cooldown;
                        await ctx.RespondAsync($"You're on cooldown. You can deposit or withdraw again in **{remaining.Hours}h {remaining.Minutes}m**.");
                        return;
                    }
                }

                // Validate amount
                if (amount <= 0)
                {
                    await ctx.RespondAsync("The amount of XP to deposit must be greater than 0.");
                    return;
                }

                // Normalize type to lowercase
                type = type.ToLower();

                // Handle deposit by type
                switch (type)
                {
                    case "voice":
                        if (await _userVoiceXPCounter.CheckUserDeposit(ctx.User.Id, amount))
                        {
                            await ctx.RespondAsync($"You don't have enough voice XP to deposit {amount}, or this would exceed the maximum bank limit of 3,000 XP.");
                            return;
                        }

                        _bankCooldowns[ctx.User.Id] = DateTime.UtcNow;
                        _userVoiceXPCounter.Deposit(ctx.User.Id, amount);

                        var taxAmount = (int)(amount * 0.1);
                        var depositedAmount = amount - taxAmount;

                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Voice XP Deposited")
                            .WithDescription($"You deposited **{depositedAmount:N0}** voice XP to the bank.")
                            .AddField("Tax", $"{taxAmount:N0} XP (10%)", true)
                            .WithColor(DiscordColor.Green)
                            .WithFooter("Bank operations have a 24-hour cooldown"));

                        _logger.LogInformation($"User {ctx.User.Id} deposited {depositedAmount} voice XP (tax: {taxAmount})");
                        break;

                    case "message":
                        if (await _userMessageXPCounter.CheckUserDeposit(ctx.User.Id, amount))
                        {
                            await ctx.RespondAsync($"You don't have enough message XP to deposit {amount}, or this would exceed the maximum bank limit of 3,000 XP.");
                            return;
                        }

                        _bankCooldowns[ctx.User.Id] = DateTime.UtcNow;
                        _userMessageXPCounter.Deposit(ctx.User.Id, amount);

                        taxAmount = (int)(amount * 0.1);
                        depositedAmount = amount - taxAmount;

                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Message XP Deposited")
                            .WithDescription($"You deposited **{depositedAmount:N0}** message XP to the bank.")
                            .AddField("Tax", $"{taxAmount:N0} XP (10%)", true)
                            .WithColor(DiscordColor.Green)
                            .WithFooter("Bank operations have a 24-hour cooldown"));

                        _logger.LogInformation($"User {ctx.User.Id} deposited {depositedAmount} message XP (tax: {taxAmount})");
                        break;

                    default:
                        await ctx.RespondAsync("Invalid XP type. Please choose either 'voice' or 'message'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing deposit for user {ctx.User.Id}");
                await ctx.RespondAsync("An error occurred while processing your deposit. Please try again later.");
            }
        }

        [Command("withdraw"), Description("Withdraw your saved XP")]
        public async Task WithdrawXP(
            CommandContext ctx,
            [Description("Type of XP to withdraw"), SlashChoiceProvider<XpTypeProvider>] string type)
        {
            try
            {
                _logger.LogInformation($"User {ctx.User.Id} attempting to withdraw {type} XP");

                // Check if user is on bank cooldown
                if (_bankCooldowns.TryGetValue(ctx.User.Id, out DateTime lastBankTime))
                {
                    TimeSpan cooldown = DateTime.UtcNow - lastBankTime;
                    if (cooldown.TotalHours < 24)
                    {
                        TimeSpan remaining = TimeSpan.FromHours(24) - cooldown;
                        await ctx.RespondAsync($"You're on cooldown. You can deposit or withdraw again in **{remaining.Hours}h {remaining.Minutes}m**.");
                        return;
                    }
                }

                // Normalize type to lowercase
                type = type.ToLower();

                // Handle withdraw by type
                switch (type)
                {
                    case "voice":
                        _bankCooldowns[ctx.User.Id] = DateTime.UtcNow;
                        int voiceXP = await _userVoiceXPCounter.Withdraw(ctx.User.Id, ctx.User);

                        if (voiceXP <= 0)
                        {
                            await ctx.RespondAsync("You don't have any voice XP stored in the bank.");
                            return;
                        }

                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Voice XP Withdrawn")
                            .WithDescription($"You withdrew **{voiceXP:N0}** voice XP from the bank.")
                            .WithColor(DiscordColor.Green)
                            .WithFooter("Bank operations have a 24-hour cooldown"));

                        _logger.LogInformation($"User {ctx.User.Id} withdrew {voiceXP} voice XP");
                        break;

                    case "message":
                        _bankCooldowns[ctx.User.Id] = DateTime.UtcNow;
                        int messageXP = await _userMessageXPCounter.Withdraw(ctx.User.Id, ctx.User);

                        if (messageXP <= 0)
                        {
                            await ctx.RespondAsync("You don't have any message XP stored in the bank.");
                            return;
                        }

                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Message XP Withdrawn")
                            .WithDescription($"You withdrew **{messageXP:N0}** message XP from the bank.")
                            .WithColor(DiscordColor.Green)
                            .WithFooter("Bank operations have a 24-hour cooldown"));

                        _logger.LogInformation($"User {ctx.User.Id} withdrew {messageXP} message XP");
                        break;

                    default:
                        await ctx.RespondAsync("Invalid XP type. Please choose either 'voice' or 'message'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing withdraw for user {ctx.User.Id}");
                await ctx.RespondAsync("An error occurred while processing your withdrawal. Please try again later.");
            }
        }

        [Command("cooldowns"), Description("Check your action cooldowns")]
        public async Task CheckCooldowns(CommandContext ctx)
        {
            try
            {
                _logger.LogInformation($"User {ctx.User.Id} checking cooldowns");

                var builder = new DiscordEmbedBuilder()
                    .WithTitle("Your Cooldowns")
                    .WithColor(new DiscordColor("#3498db"));

                // Check action cooldown (steal, guard)
                TimeSpan? actionRemaining = null;
                if (_actionCooldowns.TryGetValue(ctx.User.Id, out DateTime actionTime))
                {
                    TimeSpan elapsed = DateTime.UtcNow - actionTime;
                    if (elapsed.TotalHours < 6)
                    {
                        actionRemaining = TimeSpan.FromHours(6) - elapsed;
                        builder.AddField("Action Cooldown",
                            $"**{actionRemaining.Value.Hours}h {actionRemaining.Value.Minutes}m** remaining",
                            true);
                    }
                }

                // Check special action cooldown (heist, donate)
                TimeSpan? specialRemaining = null;
                if (_specialActionCooldowns.TryGetValue(ctx.User.Id, out DateTime specialTime))
                {
                    TimeSpan elapsed = DateTime.UtcNow - specialTime;
                    if (elapsed.TotalHours < 24)
                    {
                        specialRemaining = TimeSpan.FromHours(24) - elapsed;
                        builder.AddField("Special Action Cooldown",
                            $"**{specialRemaining.Value.Hours}h {specialRemaining.Value.Minutes}m** remaining",
                            true);
                    }
                }

                // Check bank cooldown (deposit, withdraw)
                TimeSpan? bankRemaining = null;
                if (_bankCooldowns.TryGetValue(ctx.User.Id, out DateTime bankTime))
                {
                    TimeSpan elapsed = DateTime.UtcNow - bankTime;
                    if (elapsed.TotalHours < 24)
                    {
                        bankRemaining = TimeSpan.FromHours(24) - elapsed;
                        builder.AddField("Bank Operation Cooldown",
                            $"**{bankRemaining.Value.Hours}h {bankRemaining.Value.Minutes}m** remaining",
                            true);
                    }
                }

                // Check if user is guarding someone
                KeyValuePair<ulong, ProtectionInfo>? guardInfo = _protections
                    .FirstOrDefault(p => p.Value.Protector.Id == ctx.User.Id);

                if (guardInfo.HasValue && guardInfo.Value.Value.EndTime > DateTime.UtcNow)
                {
                    TimeSpan guardRemaining = guardInfo.Value.Value.EndTime - DateTime.UtcNow;
                    builder.AddField("Currently Guarding",
                        $"**{guardInfo.Value.Value.Protector.Username}** for **{guardRemaining.Hours}h {guardRemaining.Minutes}m** more",
                        false);
                }

                // Add summary if no cooldowns
                if (!actionRemaining.HasValue && !specialRemaining.HasValue &&
                    !bankRemaining.HasValue && !guardInfo.HasValue)
                {
                    builder.WithDescription("You have no active cooldowns! All commands are available.");
                }

                await ctx.RespondAsync(builder.Build());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking cooldowns for user {ctx.User.Id}");
                await ctx.RespondAsync("An error occurred while checking your cooldowns. Please try again later.");
            }
        }

        [Command("balance"), Description("Check your bank balance")]
        public async Task CheckBalance(CommandContext ctx)
        {
            try
            {
                _logger.LogInformation($"User {ctx.User.Id} checking balance");

                int messageBalance = await _userMessageXPCounter.MessageBalance(ctx.User.Id);
                int voiceBalance = await _userVoiceXPCounter.VoiceBalance(ctx.User.Id);
                int totalBalance = messageBalance + voiceBalance;

                var builder = new DiscordEmbedBuilder()
                    .WithTitle("Bank Balance")
                    .WithDescription($"You have **{totalBalance:N0} XP** stored in the bank.")
                    .AddField("Message XP", $"{messageBalance:N0} XP", true)
                    .AddField("Voice XP", $"{voiceBalance:N0} XP", true)
                    .WithColor(new DiscordColor("#3498db"))
                    .WithFooter("Use /deposit or /withdraw to manage your XP");

                await ctx.RespondAsync(builder.Build());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking balance for user {ctx.User.Id}");
                await ctx.RespondAsync("An error occurred while checking your balance. Please try again later.");
            }
        }

        [Command("donate"), Description("Donate XP to raise your Karma")]
        public async Task DonateXP(
            CommandContext ctx,
            [Description("Type of XP to donate"), SlashChoiceProvider<XpTypeProvider>] string type)
        {
            try
            {
                _logger.LogInformation($"User {ctx.User.Id} attempting to donate {type} XP");

                // Check special action cooldown
                if (_specialActionCooldowns.TryGetValue(ctx.User.Id, out DateTime lastActionTime))
                {
                    TimeSpan cooldown = DateTime.UtcNow - lastActionTime;
                    if (cooldown.TotalHours < 24)
                    {
                        TimeSpan remaining = TimeSpan.FromHours(24) - cooldown;
                        await ctx.RespondAsync($"You're on cooldown. You can perform special actions again in **{remaining.Hours}h {remaining.Minutes}m**.");
                        return;
                    }
                }

                // Normalize type to lowercase
                type = type.ToLower();

                // Handle donation by type
                switch (type)
                {
                    case "voice":
                        if (await _userVoiceXPCounter.CheckDonateXP(ctx.User.Id))
                        {
                            await ctx.RespondAsync("You need at least 1,500 voice XP to make a donation.");
                            return;
                        }

                        _specialActionCooldowns[ctx.User.Id] = DateTime.UtcNow;
                        var voiceKarma = await _userVoiceXPCounter.Donate(ctx.User.Id);

                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Donation Successful")
                            .WithDescription("Thank you for your generous donation to the monarchy!")
                            .AddField("Donated", "1,500 voice XP", true)
                            .AddField("Karma Increase", $"+{voiceKarma:F2}", true)
                            .WithColor(DiscordColor.Gold)
                            .WithFooter("Special actions have a 24-hour cooldown"));

                        _logger.LogInformation($"User {ctx.User.Id} donated voice XP, gained {voiceKarma} karma");
                        break;

                    case "message":
                        if (await _userMessageXPCounter.CheckDonateXP(ctx.User.Id))
                        {
                            await ctx.RespondAsync("You need at least 1,500 message XP to make a donation.");
                            return;
                        }

                        _specialActionCooldowns[ctx.User.Id] = DateTime.UtcNow;
                        var messageKarma = await _userMessageXPCounter.Donate(ctx.User.Id);

                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Donation Successful")
                            .WithDescription("Thank you for your generous donation to the monarchy!")
                            .AddField("Donated", "1,500 message XP", true)
                            .AddField("Karma Increase", $"+{messageKarma:F2}", true)
                            .WithColor(DiscordColor.Gold)
                            .WithFooter("Special actions have a 24-hour cooldown"));

                        _logger.LogInformation($"User {ctx.User.Id} donated message XP, gained {messageKarma} karma");
                        break;

                    default:
                        await ctx.RespondAsync("Invalid XP type. Please choose either 'voice' or 'message'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing donation for user {ctx.User.Id}");
                await ctx.RespondAsync("An error occurred while processing your donation. Please try again later.");
            }
        }

        // Rest of your methods (heist, guard, steal) would follow the same pattern
        // I'll show one more as an example:

        [Command("heist"), Description("Attempt a bank heist for XP")]
        public async Task AttemptHeist(
            CommandContext ctx,
            [Description("Type of XP to heist"), SlashChoiceProvider<XpTypeProvider>] string type)
        {
            try
            {
                _logger.LogInformation($"User {ctx.User.Id} attempting to heist {type} XP");

                // Check special action cooldown
                if (_specialActionCooldowns.TryGetValue(ctx.User.Id, out DateTime lastActionTime))
                {
                    TimeSpan cooldown = DateTime.UtcNow - lastActionTime;
                    if (cooldown.TotalHours < 24)
                    {
                        TimeSpan remaining = TimeSpan.FromHours(24) - cooldown;
                        await ctx.RespondAsync($"You're on cooldown. You can perform special actions again in **{remaining.Hours}h {remaining.Minutes}m**.");
                        return;
                    }
                }

                var guild = ctx.Guild;
                double random = _random.NextDouble();

                // Normalize type to lowercase
                type = type.ToLower();

                // Handle heist by type
                switch (type)
                {
                    case "voice":
                        if (await _userVoiceXPCounter.CheckHeistXP(ctx.User.Id))
                        {
                            await ctx.RespondAsync("You need at least 500 voice XP to attempt a heist.");
                            return;
                        }

                        _specialActionCooldowns[ctx.User.Id] = DateTime.UtcNow;
                        var voiceHeist = await _userVoiceXPCounter.Heist(ctx.User);
                        var voiceHeistWinnings = voiceHeist.Item1;
                        var voiceLostKarma = voiceHeist.Item2;

                        if (voiceHeistWinnings > 0)
                        {
                            // Successful heist
                            await ctx.RespondAsync(new DiscordEmbedBuilder()
                                .WithTitle("Heist Successful!")
                                .WithDescription($"You pulled off the bank heist and escaped with **{voiceHeistWinnings:N0} voice XP**!")
                                .WithColor(DiscordColor.Green)
                                .WithFooter("Special actions have a 24-hour cooldown"));

                            _logger.LogInformation($"User {ctx.User.Id} successful heist: {voiceHeistWinnings} voice XP");
                        }
                        else if (voiceHeistWinnings == -1)
                        {
                            _logger.LogError($"Error processing heist for user {ctx.User.Id}");
                            await ctx.RespondAsync("An error occurred while processing your heist. Please try again later.");
                        }
                        else
                        {
                            // Failed heist
                            if (random <= 0.01)
                            {
                                await ctx.RespondAsync(new DiscordEmbedBuilder()
                                    .WithTitle("Heist Failed!")
                                    .WithDescription("You successfully broke into the bank but found absolutely nothing. Better luck next time!")
                                    .WithColor(DiscordColor.Green)
                                    .WithFooter("Special actions have a 24-hour cooldown"));
                            }
                            else if (random <= 0.05)
                            {
                                await ctx.RespondAsync(new DiscordEmbedBuilder()
                                    .WithTitle("Heist Failed!")
                                    .WithDescription("You slipped on a banana peel mid-heist. Qutie laughed and took your XP as a fine for comedic failure.")
                                    .WithColor(DiscordColor.Red)
                                    .WithFooter("Special actions have a 24-hour cooldown"));
                            }
                            else
                            {
                                await ctx.RespondAsync(new DiscordEmbedBuilder()
                                    .WithTitle("Heist Failed!")
                                    .WithDescription($"You were caught in the act! You've been fined 500 voice XP and your karma has decreased by {voiceLostKarma:N2}.")
                                    .WithColor(DiscordColor.Red)
                                    .WithFooter("Special actions have a 24-hour cooldown"));
                            }

                            _logger.LogInformation($"User {ctx.User.Id} failed voice XP heist");
                        }
                        break;

                    case "message":
                        if (await _userMessageXPCounter.CheckHeistXP(ctx.User.Id))
                        {
                            await ctx.RespondAsync("You need at least 500 message XP to attempt a heist.");
                            return;
                        }

                        _specialActionCooldowns[ctx.User.Id] = DateTime.UtcNow;
                        var messageHeist = await _userMessageXPCounter.Heist(ctx.User);
                        var messageHeistWinnings = messageHeist.Item1;
                        var messageLostKarma = messageHeist.Item2;

                        if (messageHeistWinnings > 0)
                        {
                            // Successful heist
                            await ctx.RespondAsync(new DiscordEmbedBuilder()
                                .WithTitle("Heist Successful!")
                                .WithDescription($"You pulled off the bank heist and escaped with **{messageHeistWinnings:N0} message XP**!")
                                .WithColor(DiscordColor.Green)
                                .WithFooter("Special actions have a 24-hour cooldown"));

                            _logger.LogInformation($"User {ctx.User.Id} successful heist: {messageHeistWinnings} message XP");
                        }
                        else if (messageHeistWinnings == -1)
                        {
                            _logger.LogError($"Error processing heist for user {ctx.User.Id}");
                            await ctx.RespondAsync("An error occurred while processing your heist. Please try again later.");
                        }
                        else
                        {
                            // Failed heist
                            if (random <= 0.01)
                            {
                                await ctx.RespondAsync(new DiscordEmbedBuilder()
                                    .WithTitle("Heist Failed!")
                                    .WithDescription($"You pulled off the bank heist and escaped with **{messageHeistWinnings:N0} message XP**! Lmao, you really thought you got it huh?")
                                    .WithColor(DiscordColor.Green)
                                    .WithFooter("Special actions have a 24-hour cooldown"));
                            }
                            else if (random <= 0.05)
                            {
                                await ctx.RespondAsync(new DiscordEmbedBuilder()
                                    .WithTitle("Heist Failed!")
                                    .WithDescription($"Qutie saw your attempt, sighed, and looked in disgust. You lost XP from sheer embarrassment.")
                                    .WithColor(DiscordColor.Red)
                                    .WithFooter("Special actions have a 24-hour cooldown"));
                            }
                            else
                            {
                                await ctx.RespondAsync(new DiscordEmbedBuilder()
                                    .WithTitle("Heist Failed!")
                                    .WithDescription($"You were caught in the act! You've been fined 500 message XP and your karma has decreased by {messageLostKarma:N2}.")
                                    .WithColor(DiscordColor.Red)
                                    .WithFooter("Special actions have a 24-hour cooldown"));
                            }

                            _logger.LogInformation($"User {ctx.User.Id} failed voice XP heist");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing heist for user {ctx.User.Id}");
                await ctx.RespondAsync("An error occurred while processing your heist. Please try again later.");
            }
        }

        [Command("guard"), Description("Protect another user from robbery attempts")]
        public async Task GuardUser(CommandContext ctx, [Description("User to protect")] DiscordMember target)
        {
            try
            {
                _logger.LogInformation($"User {ctx.User.Id} attempting to guard user {target.Id}");

                // Check action cooldown
                if (_actionCooldowns.TryGetValue(ctx.User.Id, out DateTime lastActionTime))
                {
                    TimeSpan cooldown = DateTime.UtcNow - lastActionTime;
                    if (cooldown.TotalHours < 6)
                    {
                        TimeSpan remaining = TimeSpan.FromHours(6) - cooldown;
                        await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
                            .WithContent($"You're on cooldown. You can perform actions again in **{remaining.Hours}h {remaining.Minutes}m**.")
                            .AsEphemeral(true));
                        return;
                    }
                }

                // Validate target
                if (target.Id == BotUserId)
                {
                    await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
                        .WithContent("You cannot protect the bot. Don't worry, my defenses are already impenetrable!")
                        .AsEphemeral(true));
                    return;
                }

                if (target.Id == ctx.User.Id)
                {
                    await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
                        .WithContent("You cannot protect yourself. Find yourself some friends")
                        .AsEphemeral(true));
                    return;
                }

                // Apply guard
                _actionCooldowns[ctx.User.Id] = DateTime.UtcNow;
                DateTime guardEndTime = DateTime.UtcNow.AddHours(3);
                _protections[target.Id] = new ProtectionInfo { Protector = ctx.Member, EndTime = guardEndTime };

                TimeSpan duration = guardEndTime - DateTime.UtcNow;

                await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
                    .WithContent($"You are now protecting **{target.DisplayName}** for the next **{duration.Hours}h {duration.Minutes}m**.")
                    .AsEphemeral(true));

                _logger.LogInformation($"User {ctx.User.Id} now guarding user {target.Id} until {guardEndTime}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing guard command for user {ctx.User.Id}");
                await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
                    .WithContent("An error occurred while processing your guard request. Please try again later.")
                    .AsEphemeral(true));
            }
        }

        [Command("steal"), Description("Steal XP from another user")]
        public async Task StealXP(
            CommandContext ctx,
            [Description("Type of XP to steal")][SlashChoiceProvider<XpTypeProvider>] string type,
            [Description("Amount to steal (1-1000)")] long amount,
            [Description("User to steal from")] DiscordMember target)
        {
            try
            {
                _logger.LogInformation($"User {ctx.User.Id} attempting to steal {amount} {type} XP from user {target.Id}");

                // Check action cooldown
                if (_actionCooldowns.TryGetValue(ctx.User.Id, out DateTime lastActionTime))
                {
                    TimeSpan cooldown = DateTime.UtcNow - lastActionTime;
                    if (cooldown.TotalHours < 6)
                    {
                        TimeSpan remaining = TimeSpan.FromHours(6) - cooldown;
                        await ctx.RespondAsync($"You're on cooldown. You can perform actions again in **{remaining.Hours}h {remaining.Minutes}m**.");
                        return;
                    }
                }

                // Validate target
                if (target.Id == BotUserId)
                {
                    await ctx.RespondAsync($"You cannot steal from the bot. If you want to attempt a heist on the bank, use `/heist` instead.");
                    return;
                }

                if (target.Id == ctx.User.Id)
                {
                    await ctx.RespondAsync(new DiscordEmbedBuilder()
                        .WithTitle("Theft Successful?")
                        .WithDescription("You successfully stole XP from yourself and gave it right back. Congratulations?")
                        .WithColor(DiscordColor.Yellow));

                    _actionCooldowns[ctx.User.Id] = DateTime.UtcNow;
                    return;
                }

                // Validate amount
                if (amount < 1 || amount > 1000)
                {
                    await ctx.RespondAsync("The amount to steal must be between 1 and 1000 XP.");
                    return;
                }

                // Normalize type to lowercase
                type = type.ToLower();

                // Check if target is protected
                bool isProtected = _protections.TryGetValue(target.Id, out ProtectionInfo protectionInfo)
                    && protectionInfo.EndTime > DateTime.UtcNow;

                // Create random outcome for failure messages
                double random = _random.NextDouble();

                // Handle theft by type
                switch (type)
                {
                    case "voice":
                        // Check if both users have sufficient XP
                        if (await _userVoiceXPCounter.CheckUserXP(ctx.User.Id, target.Id, amount))
                        {
                            await ctx.RespondAsync($"Either you or **{target.DisplayName}** don't have enough voice XP for this theft.");
                            return;
                        }

                        // Apply cooldown
                        _actionCooldowns[ctx.User.Id] = DateTime.UtcNow;

                        // Attempt theft
                        double voiceKarmaReduction = await _userVoiceXPCounter.StealXP(
                            amount,
                            ctx.User,
                            target,
                            isProtected,
                            isProtected ? protectionInfo.Protector : null
                        );

                        if (voiceKarmaReduction == 0)
                        {
                            // Successful theft
                            if (isProtected)
                            {
                                await ctx.RespondAsync(new DiscordEmbedBuilder()
                                    .WithTitle("Theft Successful!")
                                    .WithDescription($"You've stolen **{amount:N0} voice XP** from **{target.DisplayName}** despite them being protected by **{protectionInfo.Protector.DisplayName}**!")
                                    .WithColor(DiscordColor.Green));

                                // Remove protection after successful theft
                                _protections.Remove(target.Id);

                                _logger.LogInformation($"User {ctx.User.Id} successfully stole {amount} voice XP from protected user {target.Id}");
                            }
                            else
                            {
                                await ctx.RespondAsync(new DiscordEmbedBuilder()
                                    .WithTitle("Theft Successful!")
                                    .WithDescription($"You've stolen **{amount:N0} voice XP** from **{target.DisplayName}**!")
                                    .WithColor(DiscordColor.Green));

                                _logger.LogInformation($"User {ctx.User.Id} successfully stole {amount} voice XP from user {target.Id}");
                            }
                        }
                        else if (voiceKarmaReduction == -1)
                        {
                            _logger.LogError($"Error processing steal command for user {ctx.User.Id}");
                            await ctx.RespondAsync("An error occurred while processing your theft attempt. Please try again later.");
                            return;
                        }
                        else
                        {
                            // Failed theft
                            if (isProtected)
                            {
                                if (random <= 0.05)
                                {
                                    await ctx.RespondAsync(new DiscordEmbedBuilder()
                                        .WithTitle("Theft Failed!")
                                        .WithDescription($"Your theft attempt failed! **{target.DisplayName}** is under the protection of **{protectionInfo.Protector.DisplayName}** who stared deep into your soul, and you handed them your own XP out of fear.")
                                        .WithColor(DiscordColor.Red));
                                }
                                else
                                {
                                    await ctx.RespondAsync(new DiscordEmbedBuilder()
                                        .WithTitle("Theft Failed!")
                                        .WithDescription($"Your theft attempt failed! **{target.DisplayName}** was protected by **{protectionInfo.Protector.Mention}**. They caught you and took your XP as compensation. Your karma has decreased by {voiceKarmaReduction:N2}.")
                                        .WithColor(DiscordColor.Red));
                                }

                                // Remove protection after it's been triggered
                                _protections.Remove(target.Id);

                                _logger.LogInformation($"User {ctx.User.Id} failed to steal voice XP from protected user {target.Id}");
                            }
                            else
                            {
                                if (random <= 0.05)
                                {
                                    await ctx.RespondAsync(new DiscordEmbedBuilder()
                                        .WithTitle("Theft Failed!")
                                        .WithDescription($"You fumbled the bag, but at least you looked cool doing it. Maybe next time!")
                                        .WithColor(DiscordColor.Red));
                                }
                                else
                                {
                                    await ctx.RespondAsync(new DiscordEmbedBuilder()
                                        .WithTitle("Theft Failed!")
                                        .WithDescription($"Your theft attempt failed! **{target.DisplayName}** caught you and your karma has been reduced by {voiceKarmaReduction:N2}.")
                                        .WithColor(DiscordColor.Red));
                                }

                                _logger.LogInformation($"User {ctx.User.Id} failed to steal voice XP from user {target.Id}");
                            }
                        }
                        break;

                    case "message":
                        // Check if both users have sufficient XP
                        if (await _userMessageXPCounter.CheckUserXP(ctx.User.Id, target.Id, amount))
                        {
                            await ctx.RespondAsync($"Either you or **{target.DisplayName}** don't have enough message XP for this theft.");
                            return;
                        }

                        // Apply cooldown
                        _actionCooldowns[ctx.User.Id] = DateTime.UtcNow;

                        // Attempt theft
                        double messageKarmaReduction = await _userMessageXPCounter.StealXP(
                            amount,
                            ctx.User,
                            target,
                            isProtected,
                            isProtected ? protectionInfo.Protector : null
                        );

                        if (messageKarmaReduction == 0)
                        {
                            // Successful theft
                            if (isProtected)
                            {
                                await ctx.RespondAsync(new DiscordEmbedBuilder()
                                    .WithTitle("Theft Successful!")
                                    .WithDescription($"You've stolen **{amount:N0} message XP** from **{target.DisplayName}** despite them being protected by **{protectionInfo.Protector.DisplayName}**!")
                                    .WithColor(DiscordColor.Green));

                                // Remove protection after successful theft
                                _protections.Remove(target.Id);

                                _logger.LogInformation($"User {ctx.User.Id} successfully stole {amount} message XP from protected user {target.Id}");
                            }
                            else
                            {
                                await ctx.RespondAsync(new DiscordEmbedBuilder()
                                    .WithTitle("Theft Successful!")
                                    .WithDescription($"You've stolen **{amount:N0} message XP** from **{target.DisplayName}**!")
                                    .WithColor(DiscordColor.Green));

                                _logger.LogInformation($"User {ctx.User.Id} successfully stole {amount} message XP from user {target.Id}");
                            }
                        }
                        else if (messageKarmaReduction == -1)
                        {
                            _logger.LogError($"Error processing steal command for user {ctx.User.Id}");
                            await ctx.RespondAsync("An error occurred while processing your theft attempt. Please try again later.");
                            return;
                        }
                        else
                        {
                            // Failed theft
                            if (isProtected)
                            {
                                if (random <= 0.05)
                                {
                                    await ctx.RespondAsync(new DiscordEmbedBuilder()
                                        .WithTitle("Theft Failed!")
                                        .WithDescription($"You fumbled so hard, **{target.DisplayName}** and **{protectionInfo.Protector.DisplayName}** pickpocketed you instead!")
                                        .WithColor(DiscordColor.Red));
                                }
                                else
                                {
                                    await ctx.RespondAsync(new DiscordEmbedBuilder()
                                        .WithTitle("Theft Failed!")
                                        .WithDescription($"Your theft attempt failed! **{target.DisplayName}** was protected by **{protectionInfo.Protector.Mention}**. They caught you and took your XP as compensation. Your karma has decreased by {messageKarmaReduction:N2}.")
                                        .WithColor(DiscordColor.Red));
                                }

                                // Remove protection after it's been triggered
                                _protections.Remove(target.Id);

                                _logger.LogInformation($"User {ctx.User.Id} failed to steal message XP from protected user {target.Id}");
                            }
                            else
                            {
                                if (random <= 0.05)
                                {
                                    await ctx.RespondAsync(new DiscordEmbedBuilder()
                                        .WithTitle("Theft Failed!")
                                        .WithDescription($"Your theft attempt failed! **{target.DisplayName}** whooped ur ass!")
                                        .WithColor(DiscordColor.Red));
                                }
                                else
                                {
                                    await ctx.RespondAsync(new DiscordEmbedBuilder()
                                        .WithTitle("Theft Failed!")
                                        .WithDescription($"Your theft attempt failed! **{target.DisplayName}** caught you and your karma has been reduced by {messageKarmaReduction:N2}.")
                                        .WithColor(DiscordColor.Red));
                                }

                                _logger.LogInformation($"User {ctx.User.Id} failed to steal message XP from user {target.Id}");
                            }
                        }
                        break;

                    default:
                        await ctx.RespondAsync("Invalid XP type. Please choose either 'voice' or 'message'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing steal command for user {ctx.User.Id}");
                await ctx.RespondAsync("An error occurred while processing your theft attempt. Please try again later.");
            }
        }
    }

    public class ProtectionInfo
    {
        public DiscordMember Protector { get; set; }
        public DateTime EndTime { get; set; }
    }

    public class XpTypeProvider : IChoiceProvider
    {
        public ValueTask<IReadOnlyDictionary<string, object>> ProvideAsync(CommandParameter parameter)
        {
            var choices = new Dictionary<string, object>
            {
                { "Voice", "voice" },
                { "Message", "message" }
            };

            return new ValueTask<IReadOnlyDictionary<string, object>>(choices);
        }
    }
}