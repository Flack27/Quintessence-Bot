using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using QutieDTO.Models;
using QutieDAL.DAL;
using QutieBot.Bot.GoogleSheets;
using Microsoft.Extensions.Logging;
using DSharpPlus;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using QutieBot.Bot.Services;

namespace QutieBot.Bot.Commands
{
    [Command("admin")]
    public class AdminCommands
    {
        private readonly JoinToCreateManager _joinToCreateChannelBot;
        private readonly CommandsModule _commandsModule;
        private readonly GoogleSheetsFacade _googleSheetsFacade;
        private readonly AutomatedCheckService _automatedCheckService;
        private readonly CommandsDAL _commandsDAL;
        private readonly AutoRoleDAL _autoRoleDAL;
        private readonly InterviewFollowUpService _interviewFollowUpService;
        private readonly ILogger<AdminCommands> _logger;


        public AdminCommands(
            JoinToCreateManager joinToCreateChannelBot,
            CommandsModule commandsModule,
            GoogleSheetsFacade googleSheetsFacade,
            CommandsDAL commandsDAL,
            AutomatedCheckService automatedCheckService,
            AutoRoleDAL autoRoleDAL,
            InterviewFollowUpService interviewFollowUpService,
            ILogger<AdminCommands> logger)
        {
            _joinToCreateChannelBot = joinToCreateChannelBot ?? throw new ArgumentNullException(nameof(joinToCreateChannelBot));
            _commandsModule = commandsModule ?? throw new ArgumentNullException(nameof(commandsModule));
            _googleSheetsFacade = googleSheetsFacade ?? throw new ArgumentNullException(nameof(googleSheetsFacade));
            _automatedCheckService = automatedCheckService ?? throw new ArgumentNullException(nameof(automatedCheckService));
            _interviewFollowUpService = interviewFollowUpService ?? throw new ArgumentNullException(nameof(interviewFollowUpService));
            _commandsDAL = commandsDAL ?? throw new ArgumentNullException(nameof(commandsDAL));
            _autoRoleDAL = autoRoleDAL ?? throw new ArgumentNullException(nameof(autoRoleDAL));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        }


        [Command("commands"), Description("Show all available admin commands")]
        [RequirePermissions(DiscordPermissions.ModerateMembers)]
        public async Task ShowAdminCommands(CommandContext ctx)
        {
            try
            {
                _logger.LogInformation($"User {ctx.User.Id} requested admin command list");

                var commandsList = new StringBuilder();

                // Channel Management Commands
                commandsList.AppendLine("🔧 **Channel Management**");
                commandsList.AppendLine("`/admin channel set` - Designate a voice channel as Join to Create");
                commandsList.AppendLine("`/admin channel remove` - Remove a voice channel as Join to Create");
                commandsList.AppendLine("`/admin channel list` - Show all join-to-create channels");
                commandsList.AppendLine();

                // Game Management Commands
                commandsList.AppendLine("🎮 **Game Management**");
                commandsList.AppendLine("`/admin game list` - List all tracked games with Google Sheets");
                commandsList.AppendLine("`/admin game create` - Add a new game to the tracking system");
                commandsList.AppendLine("`/admin game remove` - Remove a game from the tracking system");
                commandsList.AppendLine();

                // Sheets & Sync Commands
                commandsList.AppendLine("📊 **Google Sheets Integration**");
                commandsList.AppendLine("`/admin sheet sync` - Synchronize all users and events with Google Sheets");
                commandsList.AppendLine("`/admin sheet notify` - Send notifications to users with missing game info");
                commandsList.AppendLine();

                // Auto-Role Management Commands (add after Event Management section)
                commandsList.AppendLine("🎭 **Auto-Role Management**");
                commandsList.AppendLine("`/admin autorole add` - Add a role to auto-assign to new members");
                commandsList.AppendLine("`/admin autorole remove` - Remove a role from auto-assignment");
                commandsList.AppendLine("`/admin autorole list` - List all auto-assigned roles");
                commandsList.AppendLine();

                // Event Management Commands
                commandsList.AppendLine("📅 **Event Management**");
                commandsList.AppendLine("`/admin event attendees` - Check who signed up but isn't in voice");
                commandsList.AppendLine("`/admin event signups` - List users who haven't signed up for an event");
                commandsList.AppendLine("`/admin event dm` - Send DMs to users who haven't signed up");
                commandsList.AppendLine("`/admin event remove` - Remove a user's attendance from past events.");
                commandsList.AppendLine("`/admin event add` -  Add a user's attendance to past events.");
                commandsList.AppendLine();

                // Reaction Role Commands
                commandsList.AppendLine("🔄 **Reaction Roles**");
                commandsList.AppendLine("`/admin role add` - Add a reaction role to a message");
                commandsList.AppendLine("`/admin role remove` - Remove a reaction role from a message");
                commandsList.AppendLine("`/admin role list` - List all reaction roles in the server");

                var embed = new DiscordEmbedBuilder()
                    .WithTitle("Administrator Commands")
                    .WithDescription(commandsList.ToString())
                    .WithColor(DiscordColor.Purple)
                    .WithFooter("All commands require Moderator permissions");

                await ctx.RespondAsync(embed.Build());

                _logger.LogInformation($"Successfully displayed admin commands for user {ctx.User.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error displaying admin commands for user {ctx.User.Id}");
                await ctx.RespondAsync("An error occurred while retrieving admin commands. Please try again later.");
            }
        }

        [Command("channel")]
        public class ChannelCommands
        {
            private readonly AdminCommands _parent;

            public ChannelCommands(AdminCommands parent)
            {
                _parent = parent;
            }

            [Command("set"), Description("Set a voice channel as a join-to-create channel")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task SetJoinToCreateChannel(
            CommandContext ctx,
            [Description("Voice channel to designate")] DiscordChannel channel)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} attempting to set channel {channel.Id} as join-to-create");

                    // Validate channel type
                    if (channel.Type != DiscordChannelType.Voice)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid Channel")
                            .WithDescription("You must select a voice channel.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid channel type for join-to-create");
                        return;
                    }

                    // Set the channel
                    bool success = await _parent._joinToCreateChannelBot.StoreJoinToCreateChannel(
                        (long)channel.Id,
                        channel.Name,
                        channel.Parent?.Name ?? "No Category");

                    if (success)
                    {
                        await _parent._joinToCreateChannelBot.InitializeAsync();

                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Join-to-Create Channel Set")
                            .WithDescription($"{channel.Mention} has been set as a join-to-create channel.")
                            .WithColor(DiscordColor.Green));

                        _parent._logger.LogInformation($"User {ctx.User.Id} successfully set channel {channel.Id} as join-to-create");
                    }
                    else
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Setup Failed")
                            .WithDescription("Failed to set the join-to-create channel. Please try again later.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} failed to set channel {channel.Id} as join-to-create");
                    }
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error setting join-to-create channel {channel?.Id} for user {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while setting the join-to-create channel. Please try again later.");
                }
            }

            [Command("remove"), Description("Remove a voice channel as a join-to-create channel")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task RemoveJoinToCreateChannel(
            CommandContext ctx,
            [Description("Voice channel to designate")] DiscordChannel channel)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} attempting to remove channel {channel.Id} as join-to-create");

                    // Validate channel type
                    if (channel.Type != DiscordChannelType.Voice)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid Channel")
                            .WithDescription("You must select a voice channel.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid channel type for join-to-create");
                        return;
                    }

                    // Set the channel
                    int success = await _parent._joinToCreateChannelBot.DeleteJoinToCreateChannel((long)channel.Id);

                    if (success == 0)
                    {
                        await _parent._joinToCreateChannelBot.InitializeAsync();

                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Join-to-Create Channel Not Found")
                            .WithDescription($"{channel.Mention} has not been set as a join-to-create channel.")
                            .WithColor(DiscordColor.Green));

                        _parent._logger.LogInformation($"User {ctx.User.Id} selected wrong channel {channel.Id} as join-to-create");
                    }
                    else if (success == 1)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Join-to-Create Channel Removed")
                            .WithDescription($"{channel.Mention} has been removed as a join-to-create channel.")
                            .WithColor(DiscordColor.Green));

                        _parent._logger.LogInformation($"User {ctx.User.Id} successfully removed channel {channel.Id} as join-to-create");
                    }
                    else
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                        .WithTitle("Removal Failed")
                        .WithDescription("Failed to remove the join-to-create channel. Please try again later.")
                        .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} failed to set channel {channel.Id} as join-to-create");
                    }
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error removing join-to-create channel {channel?.Id} for user {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while removing the join-to-create channel. Please try again later.");
                }
            }

            [Command("list"), Description("List all join-to-create channels")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task ListJoinToCreateChannels(CommandContext ctx)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} requested join-to-create channel list");

                    var channels = await _parent._joinToCreateChannelBot.GetChannels();

                    if (channels == null || !channels.Any())
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("No Channels Found")
                            .WithDescription("There are no join-to-create channels currently set up.")
                            .WithColor(DiscordColor.Yellow));

                        _parent._logger.LogInformation($"No join-to-create channels found for user {ctx.User.Id}");
                        return;
                    }

                    var embed = new DiscordEmbedBuilder()
                        .WithTitle("Join-to-Create Channels")
                        .WithColor(DiscordColor.Blurple);

                    foreach (var channel in channels)
                    {
                        embed.AddField(
                            channel.ChannelName,
                            $"**Category:** {channel.Category}",
                            true);
                    }

                    await ctx.RespondAsync(embed.Build());

                    _parent._logger.LogInformation($"Successfully displayed {channels.Count()} join-to-create channels for user {ctx.User.Id}");
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error listing join-to-create channels for user {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while retrieving the channel list. Please try again later.");
                }
            }
        }


        [Command("sheet")]
        public class SheetCommands
        {
            private readonly AdminCommands _parent;

            public SheetCommands(AdminCommands parent)
            {
                _parent = parent;
            }

            [Command("sync"), Description("Synchronize all users, active events and signups with Google Sheets")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task SyncEvents(CommandContext ctx)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} initiating sync with Google Sheets");

                    await ctx.DeferResponseAsync();

                    await _parent._googleSheetsFacade.SyncUserDataAsync();
                    await _parent._commandsModule.UpdateEvents();
                    await _parent._automatedCheckService.MonitorEvents();

                    var embed = new DiscordEmbedBuilder()
                        .WithTitle("Sync Completed")
                        .WithDescription("Events and users have been successfully synced with Google Sheets")
                        .WithColor(DiscordColor.Green)
                        .WithTimestamp(DateTime.Now);

                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));

                    _parent._logger.LogInformation($"Successfully completed event sync for user {ctx.User.Id}");
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error syncing with Google Sheets for user {ctx.User.Id}");
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        "An error occurred while syncing with Google Sheets. Please try again later."));
                }
            }


            [Command("notify"), Description("Notify users who haven't added their game information")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task NotifyMissingInfo(CommandContext ctx)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} initiating notification for users with missing info");

                    await ctx.DeferResponseAsync();

                    await _parent._googleSheetsFacade.SyncUserDataAsync(sendNotifications: true);

                    var embed = new DiscordEmbedBuilder()
                        .WithTitle("Notifications Sent")
                        .WithDescription("Users with missing information have been notified")
                        .WithColor(DiscordColor.Green)
                        .WithTimestamp(DateTime.Now);

                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));

                    _parent._logger.LogInformation($"Successfully sent notifications for users with missing info, initiated by user {ctx.User.Id}");
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error notifying users with missing info, initiated by user {ctx.User.Id}");
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        "An error occurred while notifying users. Please try again later."));
                }
            }
        }


        [Command("games")]
        public class GamesCommand
        {
            private readonly AdminCommands _parent;

            public GamesCommand(AdminCommands parent)
            {
                _parent = parent;
            }

            [Command("create"), Description("Add a new game to the tracking system")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task CreateGame(
            CommandContext ctx,
            [Description("Role for the game")] DiscordRole role,
            [Description("Game name")] string gameName,
            [Description("Google Sheet ID")] string sheetId,
            [Description("Commands channel")] DiscordChannel commandsChannel)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} attempting to create game '{gameName}' with role {role.Id} and sheet {sheetId}");

                    // Validate channel type
                    if (commandsChannel.Type != DiscordChannelType.Text)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid Channel")
                            .WithDescription("The commands channel must be a text channel.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid channel type for game '{gameName}'");
                        return;
                    }

                    // Create game object
                    var game = new Game
                    {
                        GameName = gameName,
                        RoleId = (long)role.Id,
                        SheetId = sheetId,
                        ChannelId = (long)commandsChannel.Id
                    };

                    // Save game data
                    var success = await _parent._commandsDAL.SaveGameData(game);

                    if (success)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Game Created")
                            .WithDescription($"**{gameName}** has been successfully added to the tracking system!")
                            .AddField("Role", role.Mention, true)
                            .AddField("Commands Channel", commandsChannel.Mention, true)
                            .WithColor(DiscordColor.Green));

                        _parent._logger.LogInformation($"User {ctx.User.Id} successfully created game '{gameName}'");
                    }
                    else
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Creation Failed")
                            .WithDescription("Failed to create the game. Please try again later.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} failed to create game '{gameName}'");
                    }
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error creating game '{gameName}' for user {ctx.User.Id}");
                    await ctx.RespondAsync(new DiscordEmbedBuilder()
                        .WithTitle("Error")
                        .WithDescription("An error occurred while creating the game. Please try again later.")
                        .WithColor(DiscordColor.Red));
                }
            }

            [Command("list"), Description("List all tracked games with Google Sheets")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task ListGames(CommandContext ctx)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} requested game list");

                    var games = await _parent._commandsDAL.ShowGameData();

                    if (games == null || !games.Any())
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("No Games Found")
                            .WithDescription("There are no games currently being tracked.")
                            .WithColor(DiscordColor.Yellow));

                        _parent._logger.LogInformation($"No tracked games found for user {ctx.User.Id}");
                        return;
                    }

                    var embed = new DiscordEmbedBuilder()
                        .WithTitle("Tracked Games")
                        .WithColor(DiscordColor.Blurple);

                    foreach (var game in games)
                    {
                        var fieldContent = new StringBuilder();
                        fieldContent.AppendLine($"**Role:** {game.Role?.RoleName ?? "None"}");
                        fieldContent.AppendLine($"**Channel:** {game.Channel?.ChannelName ?? "None"}");
                        fieldContent.AppendLine($"**Sheet ID:** {game.SheetId}");

                        embed.AddField(game.GameName, fieldContent.ToString(), false);
                    }

                    await ctx.RespondAsync(embed.Build());

                    _parent._logger.LogInformation($"Successfully displayed {games.Count()} tracked games for user {ctx.User.Id}");
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error listing games for user {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while retrieving the game list. Please try again later.");
                }
            }

            [Command("remove"), Description("Remove a game from the tracking system")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task RemoveGame(CommandContext ctx)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} initiating game removal");

                    var games = await _parent._commandsDAL.ShowGameData();

                    if (games == null || !games.Any())
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("No Games Found")
                            .WithDescription("There are no games currently being tracked.")
                            .WithColor(DiscordColor.Yellow));

                        _parent._logger.LogInformation($"No tracked games found for removal by user {ctx.User.Id}");
                        return;
                    }

                    // Create select menu with games
                    var selectMenu = new DiscordSelectComponent(
                        "game_select",
                        "Select a game to remove",
                        games.Select(e => new DiscordSelectComponentOption(e.GameName, e.GameId.ToString())).ToList());

                    // Send selection message
                    var builder = new DiscordMessageBuilder()
                        .WithContent("Please select the game you want to remove:")
                        .AddComponents(selectMenu);

                    await ctx.RespondAsync(builder);

                    var responseMessage = await ctx.GetResponseAsync();

                    if (responseMessage == null)
                    {
                        _parent._logger.LogWarning($"Failed to get response message for game removal by user {ctx.User.Id}");
                        return;
                    }

                    // Wait for user selection
                    var result = await responseMessage.WaitForSelectAsync(ctx.User, "game_select", TimeSpan.FromMinutes(2));

                    if (result.TimedOut)
                    {
                        await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                            .WithContent("You took too long to respond. Please try again."));

                        _parent._logger.LogInformation($"Game selection timed out for user {ctx.User.Id}");
                        return;
                    }

                    // Parse selected game ID
                    if (!long.TryParse(result.Result.Values.First(), out long gameId))
                    {
                        await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                            .WithContent("Invalid game selection. Please try again."));

                        _parent._logger.LogWarning($"Invalid game ID selected by user {ctx.User.Id}");
                        return;
                    }

                    // Get selected game name for confirmation
                    string gameName = games.FirstOrDefault(g => g.GameId == gameId)?.GameName ?? "Unknown Game";

                    // Remove the game
                    var success = await _parent._commandsDAL.RemoveGame(gameId);

                    if (success)
                    {
                        await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                            .WithContent($"**{gameName}** has been successfully removed from the tracking system."));

                        _parent._logger.LogInformation($"User {ctx.User.Id} successfully removed game {gameId} ({gameName})");
                    }
                    else
                    {
                        await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                            .WithContent($"Failed to remove **{gameName}**. Please try again later."));

                        _parent._logger.LogWarning($"User {ctx.User.Id} failed to remove game {gameId} ({gameName})");
                    }
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error removing game for user {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while removing the game. Please try again later.");
                }
            }

        }


        [Command("event")]
        public class EventCommands
        {
            private readonly AdminCommands _parent;

            public EventCommands(AdminCommands parent)
            {
                _parent = parent;
            }


            [Command("attendees"), Description("Check who signed up but isn't in voice")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task CheckAttendees(
            CommandContext ctx,
            [Description("Event channel to check")] DiscordChannel eventChannel,
            [Description("Voice channel to verify presence")] DiscordChannel voiceChannel)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} checking attendees for event channel {eventChannel.Id} and voice channel {voiceChannel.Id}");

                    // Validate channel types
                    if (eventChannel == null || voiceChannel == null ||
                        (voiceChannel.Type != DiscordChannelType.Voice && voiceChannel.Type != DiscordChannelType.Stage))
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid Channels")
                            .WithDescription("Please provide valid event and voice channels.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid channels for checking attendees");
                        return;
                    }

                    // Select the event
                    long? selectedEventId = await _parent._commandsModule.SelectEventAsync(ctx, eventChannel);
                    if (!selectedEventId.HasValue)
                    {
                        _parent._logger.LogInformation($"User {ctx.User.Id} did not select an event for checking attendees");
                        return;
                    }

                    // Get users who signed up but aren't in voice
                    List<SignUpData> users = await _parent._commandsModule.GetUsersNotInVoiceChannel(voiceChannel.Id, selectedEventId.Value);

                    if (users.Count == 0)
                    {
                        await ctx.FollowupAsync(new DiscordWebhookBuilder()
                            .WithContent($"✅ All users that signed up for the event in {eventChannel.Mention} are present in voice."));

                        _parent._logger.LogInformation($"All attendees present for event {selectedEventId.Value} checked by user {ctx.User.Id}");
                        return;
                    }

                    users = users.OrderByDescending(user => user.specName == "Late" || user.className == "Late" || user.roleName == "Late").ToList();

                    // Show list of missing users
                    var response = new StringBuilder();
                    response.AppendLine($"**{users.Count} users signed up but are not in {voiceChannel.Mention}:**");

                    foreach (var user in users)
                    {
                        if (user.specName == "Late" || user.className == "Late" || user.roleName == "Late")
                        {
                            response.AppendLine($"⏰ [LATE] {user.name}");
                        }
                        else
                        {
                            response.AppendLine($"❌ {user.name}");
                        }
                    }

                    response.AppendLine("\nWhat would you like to do?");

                    // Add action buttons
                    var removeAllButton = new DiscordButtonComponent(
                        DiscordButtonStyle.Danger,
                        "remove_all_attendance",
                        "Remove All Attendance");

                    var selectButton = new DiscordButtonComponent(
                        DiscordButtonStyle.Primary,
                        "confirm_remove_attendance",
                        "Select Users");

                    var cancelButton = new DiscordButtonComponent(
                        DiscordButtonStyle.Secondary,
                        "cancel_remove_attendance",
                        "Cancel");

                    var messageBuilder = new DiscordWebhookBuilder()
                        .WithContent(response.ToString())
                        .AddComponents(removeAllButton, selectButton, cancelButton);

                    var responseMsg = await ctx.FollowupAsync(messageBuilder);

                    // Wait for button selection
                    var buttonResult = await responseMsg.WaitForButtonAsync(ctx.User, TimeSpan.FromMinutes(2));

                    if (buttonResult.TimedOut)
                    {
                        await ctx.EditFollowupAsync(responseMsg.Id, new DiscordWebhookBuilder()
                            .WithContent("You took too long to respond. Please try again."));

                        _parent._logger.LogInformation($"Button selection timed out for user {ctx.User.Id} checking attendees");
                        return;
                    }

                    if (buttonResult.Result.Id == "remove_all_attendance")
                    {
                        await ctx.EditFollowupAsync(responseMsg.Id, new DiscordWebhookBuilder()
                            .WithContent(response.ToString() + "\n\n**Processing removal for all users...**"));

                        var failedUsers = new List<string>();
                        var failedUsersDb = new List<string>();

                        foreach (var user in users)
                        {
                            var result = await _parent._commandsModule.RemoveAttendance(selectedEventId.Value, (long)user.userId);
                            if (!result)
                            {
                                failedUsers.Add(user.name);
                                continue;
                            }
                        }

                        await _parent._commandsModule.UpdateEvents();

                        if (failedUsers.Count == 0 && failedUsersDb.Count == 0)
                        {
                            await ctx.EditFollowupAsync(responseMsg.Id, new DiscordWebhookBuilder()
                                .WithContent($"✅ Attendance has been successfully removed for all {users.Count} selected users."));

                            _parent._logger.LogInformation($"User {ctx.User.Id} removed attendance for all {users.Count} users from event {selectedEventId.Value}");
                        }
                        else
                        {
                            var finalResponse = new StringBuilder();
                            finalResponse.AppendLine($"⚠️ Results of attendance removal operation:");

                            if (failedUsers.Count > 0)
                            {
                                finalResponse.AppendLine($"\n❌ Failed to remove from RaidHelper ({failedUsers.Count}):");
                                finalResponse.AppendLine(string.Join(", ", failedUsers));
                            }

                            if (failedUsersDb.Count > 0)
                            {
                                finalResponse.AppendLine($"\n❌ Failed to remove from database ({failedUsersDb.Count}):");
                                finalResponse.AppendLine(string.Join(", ", failedUsersDb));
                            }

                            await ctx.EditFollowupAsync(responseMsg.Id, new DiscordWebhookBuilder()
                                .WithContent(finalResponse.ToString()));

                            _parent._logger.LogWarning($"User {ctx.User.Id} had partial failures removing attendance: {failedUsers.Count} RaidHelper failures, {failedUsersDb.Count} DB failures");
                        }
                    }
                    else if (buttonResult.Result.Id == "confirm_remove_attendance")
                    {
                        await ctx.EditFollowupAsync(responseMsg.Id, new DiscordWebhookBuilder()
                            .WithContent(response.ToString()));

                        // Create selection options (up to 25 per menu due to Discord limits)
                        var selectOptions = users.Take(25)
                            .Select(u => new DiscordSelectComponentOption(u.name, u.userId.ToString()))
                            .ToList();

                        var overflowOptions = users.Skip(25)
                            .Select(u => new DiscordSelectComponentOption(u.name, u.userId.ToString()))
                            .ToList();

                        // Create selection menu(s)
                        var userSelectMenu1 = new DiscordSelectComponent(
                            "user_select_1",
                            "Select users (1-25)",
                            selectOptions,
                            minOptions: 1,
                            maxOptions: selectOptions.Count);

                        var userSelectBuilder = new DiscordWebhookBuilder()
                            .WithContent($"Select the users whose attendance you want to remove:")
                            .AddComponents(userSelectMenu1);

                        if (overflowOptions.Count > 0)
                        {
                            var userSelectMenu2 = new DiscordSelectComponent(
                                "user_select_2",
                                "Select users (26+)",
                                overflowOptions,
                                minOptions: 1,
                                maxOptions: overflowOptions.Count);

                            userSelectBuilder.AddComponents(userSelectMenu2);
                        }

                        var userSelectResponseMsg = await ctx.FollowupAsync(userSelectBuilder);

                        // Get user selections from first menu
                        var selectResult1 = await userSelectResponseMsg.WaitForSelectAsync(ctx.User, "user_select_1", TimeSpan.FromMinutes(2));

                        // Check for second menu if it exists
                        var selectResult2 = new InteractivityResult<ComponentInteractionCreatedEventArgs>();
                        bool checkResult2 = overflowOptions.Count > 0;

                        if (checkResult2)
                        {
                            selectResult2 = await userSelectResponseMsg.WaitForSelectAsync(ctx.User, "user_select_2", TimeSpan.FromMinutes(2));
                        }

                        if (selectResult1.TimedOut || (checkResult2 && selectResult2.TimedOut))
                        {
                            await ctx.EditFollowupAsync(userSelectResponseMsg.Id, new DiscordWebhookBuilder()
                                .WithContent("You took too long to respond. Please try again."));

                            _parent._logger.LogInformation($"User selection timed out for user {ctx.User.Id}");
                            return;
                        }

                        // Combine selections
                        var selectedUserIds = selectResult1.Result.Values.Select(long.Parse).ToList();

                        if (checkResult2 && selectResult2.Result != null)
                        {
                            selectedUserIds.AddRange(selectResult2.Result.Values.Select(long.Parse));
                        }

                        // Update message to show processing is underway
                        await ctx.EditFollowupAsync(userSelectResponseMsg.Id, new DiscordWebhookBuilder()
                            .WithContent($"Processing removal for {selectedUserIds.Count} selected users..."));

                        var failedUsers = new List<string>();
                        var failedUsersDb = new List<string>();

                        // Process each selected user
                        foreach (var userId in selectedUserIds)
                        {
                            // Remove from RaidHelper
                            var result = await _parent._commandsModule.RemoveAttendance(selectedEventId.Value, userId);
                            if (!result)
                            {
                                var userName = users.FirstOrDefault(u => (long)u.userId == userId)?.name ?? "Unknown User";
                                failedUsers.Add(userName);
                                continue;
                            }
                        }

                        await _parent._commandsModule.UpdateEvents();

                        // Format response based on results
                        if (failedUsers.Count == 0 && failedUsersDb.Count == 0)
                        {
                            await ctx.EditFollowupAsync(userSelectResponseMsg.Id, new DiscordWebhookBuilder()
                                .WithContent($"✅ Attendance has been successfully removed for all {selectedUserIds.Count} selected users."));

                            _parent._logger.LogInformation($"User {ctx.User.Id} removed attendance for {selectedUserIds.Count} selected users from event {selectedEventId.Value}");
                        }
                        else
                        {
                            var finalResponse = new StringBuilder();
                            finalResponse.AppendLine($"⚠️ Results of attendance removal operation:");
                            finalResponse.AppendLine($"• Successfully processed: {selectedUserIds.Count - (failedUsers.Count + failedUsersDb.Count)} users");

                            if (failedUsers.Count > 0)
                            {
                                finalResponse.AppendLine($"\n❌ Failed to remove from RaidHelper ({failedUsers.Count}):");
                                finalResponse.AppendLine(string.Join(", ", failedUsers));
                            }

                            if (failedUsersDb.Count > 0)
                            {
                                finalResponse.AppendLine($"\n❌ Failed to remove from database ({failedUsersDb.Count}):");
                                finalResponse.AppendLine(string.Join(", ", failedUsersDb));
                            }

                            await ctx.EditFollowupAsync(userSelectResponseMsg.Id, new DiscordWebhookBuilder()
                                .WithContent(finalResponse.ToString()));

                            _parent._logger.LogWarning($"User {ctx.User.Id} had partial failures removing selected attendance: {failedUsers.Count} RaidHelper failures, {failedUsersDb.Count} DB failures");
                        }
                    }
                    else if (buttonResult.Result.Id == "cancel_remove_attendance")
                    {
                        await ctx.EditFollowupAsync(responseMsg.Id, new DiscordWebhookBuilder()
                            .WithContent("Operation canceled. No changes were made."));

                        _parent._logger.LogInformation($"User {ctx.User.Id} canceled attendance removal operation");
                    }
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error checking attendees for user {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while checking attendees. Please try again later.");
                }
            }

            [Command("signups"), Description("List users who haven't signed up for an event")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task CheckSignups(CommandContext ctx, [Description("Event channel to check")] DiscordChannel eventChannel)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} checking signups for event channel {eventChannel.Id}");

                    // Validate channel
                    if (eventChannel == null)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid Channel")
                            .WithDescription("Please provide a valid event channel.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid channel for checking signups");
                        return;
                    }

                    // Select the event
                    long? selectedEventId = await _parent._commandsModule.SelectEventAsync(ctx, eventChannel);
                    if (!selectedEventId.HasValue)
                    {
                        _parent._logger.LogInformation($"User {ctx.User.Id} did not select an event for checking signups");
                        return;
                    }

                    // Get users in channel and users who haven't signed up
                    List<DiscordMember> channelUsers = await _parent._commandsModule.GetmembersInChannel(eventChannel.Id);
                    List<DiscordMember> usersNotSignedUp = await _parent._commandsModule.GetNonSignups(eventChannel.Id, selectedEventId.Value);

                    if (usersNotSignedUp.Count == 0)
                    {
                        await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                            .WithContent($"✅ All users in {eventChannel.Mention} have signed up for the event."));

                        _parent._logger.LogInformation($"All users signed up for event {selectedEventId.Value} in channel {eventChannel.Id}, checked by user {ctx.User.Id}");
                        return;
                    }

                    // Calculate statistics
                    double usersSignedUpCount = channelUsers.Count - usersNotSignedUp.Count;
                    double percentageSignedUp = usersSignedUpCount / channelUsers.Count * 100;
                    string percentageString = $"{(int)Math.Round(percentageSignedUp)}%";

                    // Format user list with dynamic field splitting
                    var unsignedUserNames = usersNotSignedUp.Select(user => user.DisplayName).ToList();

                    // Build response
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle($"Signup Status for {eventChannel.Name}")
                        .WithDescription($"{usersNotSignedUp.Count} users haven't signed up for the event.")
                        .AddField("Progress", $"{percentageString} of users have signed up ({usersSignedUpCount}/{channelUsers.Count})", false)
                        .WithColor(DiscordColor.Yellow)
                        .WithTimestamp(DateTime.Now);

                    // Add users with dynamic field splitting (1024 char limit per field)
                    const int maxFieldLength = 1024;
                    var currentFieldUsers = new List<string>();
                    int currentLength = 0;
                    int fieldCount = 0;

                    for (int i = 0; i < unsignedUserNames.Count; i++)
                    {
                        string userName = unsignedUserNames[i];
                        int userLength = userName.Length + 1; // +1 for newline

                        // Check if adding this user would exceed the limit
                        if (currentLength + userLength > maxFieldLength && currentFieldUsers.Count > 0)
                        {
                            // Add current field and start a new one
                            string fieldName = fieldCount == 0 ? "Users Missing Signups" : $"More Users ({fieldCount + 1})";
                            embed.AddField(fieldName, string.Join("\n", currentFieldUsers), false);

                            currentFieldUsers.Clear();
                            currentLength = 0;
                            fieldCount++;
                        }

                        currentFieldUsers.Add(userName);
                        currentLength += userLength;
                    }

                    // Add remaining users
                    if (currentFieldUsers.Count > 0)
                    {
                        string fieldName = fieldCount == 0 ? "Users Missing Signups" : $"More Users ({fieldCount + 1})";
                        embed.AddField(fieldName, string.Join("\n", currentFieldUsers), false);
                    }

                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));

                    _parent._logger.LogInformation($"Successfully displayed signup status for event {selectedEventId.Value} in channel {eventChannel.Id}, checked by user {ctx.User.Id}");
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error checking signups for user {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while checking signups. Please try again later.");
                }
            }

            [Command("remove"), Description("Remove attendance for a user from an event")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task RemoveAttendance(
                CommandContext ctx,
                [Description("User to remove attendance for")] DiscordMember user)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} attempting to remove attendance for user {user.Id}");

                    // Validate user
                    if (user == null)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid User")
                            .WithDescription("Please provide a valid user.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid user for attendance removal");
                        return;
                    }

                    // Select event from database (for past events)
                    long? selectedEventId = await _parent._commandsModule.SelectEventFromDb(ctx);
                    if (!selectedEventId.HasValue)
                    {
                        _parent._logger.LogInformation($"User {ctx.User.Id} did not select an event from database");
                        return;
                    }

                    // Remove attendance from database
                    var result = await _parent._commandsModule.RemoveAttendance(selectedEventId.Value, (long)user.Id);

                    // Get event and update Google Sheets
                    var eventData = await _parent._commandsDAL.GetEventWithChannel(selectedEventId.Value);
                    await _parent._googleSheetsFacade.ProcessEventSignupAsync((long)user.Id, eventData, false);

                    // Notify result
                    if (!result)
                    {
                        await ctx.FollowupAsync(new DiscordWebhookBuilder()
                            .WithContent($"⚠️ Failed to remove attendance from the database for **{user.DisplayName}**."));

                        _parent._logger.LogWarning($"Failed to remove attendance from database for user {user.Id} from event {selectedEventId.Value}");
                    }
                    else
                    {
                        var embed = new DiscordEmbedBuilder()
                            .WithTitle("Attendance Removed")
                            .WithDescription($"Attendance has been removed for **{user.DisplayName}**")
                            .AddField("Event ID", selectedEventId.Value.ToString(), true)
                            .WithColor(DiscordColor.Orange)
                            .WithTimestamp(DateTime.Now)
                            .WithFooter("This event was selected from the database");

                        await ctx.FollowupAsync(new DiscordWebhookBuilder().AddEmbed(embed));

                        _parent._logger.LogInformation($"Successfully removed attendance for user {user.Id} from event {selectedEventId.Value}");
                    }
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error removing attendance for user {user?.Id} by admin {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while removing attendance. Please try again later.");
                }
            }

            [Command("add"), Description("Add attendance for a user to an event")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task AddAttendance(
                CommandContext ctx,
                [Description("User to add attendance for")] DiscordMember user)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} attempting to add attendance for user {user.Id}");

                    // Validate user
                    if (user == null)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid User")
                            .WithDescription("Please provide a valid user.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid user for attendance addition");
                        return;
                    }

                    // Select event from database (for past events)
                    long? selectedEventId = await _parent._commandsModule.SelectEventFromDb(ctx);
                    if (!selectedEventId.HasValue)
                    {
                        _parent._logger.LogInformation($"User {ctx.User.Id} did not select an event from database");
                        return;
                    }

                    // Add attendance to database
                    var result = await _parent._commandsModule.AddAttendance(selectedEventId.Value, (long)user.Id);
                    if (!result)
                    {
                        await ctx.FollowupAsync(new DiscordWebhookBuilder()
                            .WithContent($"⚠️ Failed to add attendance to the database for **{user.DisplayName}**."));

                        _parent._logger.LogWarning($"Failed to add attendance to database for user {user.Id} to event {selectedEventId.Value}");
                    }

                    // Get event and update Google Sheets
                    var eventData = await _parent._commandsDAL.GetEventWithChannel(selectedEventId.Value);
                    await _parent._googleSheetsFacade.ProcessEventSignupAsync((long)user.Id, eventData, true);

                    var embed = new DiscordEmbedBuilder()
                        .WithTitle("Attendance Added")
                        .WithDescription($"Attendance has been added for **{user.DisplayName}**")
                        .AddField("Event ID", selectedEventId.Value.ToString(), true)
                        .WithColor(DiscordColor.Green)
                        .WithTimestamp(DateTime.Now)
                        .WithFooter("This event was selected from the database");

                    await ctx.FollowupAsync(new DiscordWebhookBuilder().AddEmbed(embed));

                    _parent._logger.LogInformation($"Successfully added attendance for user {user.Id} to event {selectedEventId.Value}");
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error adding attendance for user {user?.Id} by admin {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while adding attendance. Please try again later.");
                }
            }

            [Command("dm"), Description("Send DMs to users who haven't signed up for an event")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task NotifyNonSignups(
                CommandContext ctx,
                [Description("Event channel to check")] DiscordChannel eventChannel,
                [Description("Message to send to users")] string message)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} initiating DM to non-signups for event channel {eventChannel.Id}");

                    // Validate channel
                    if (eventChannel == null)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid Channel")
                            .WithDescription("Please provide a valid event channel.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid channel for DM non-signups");
                        return;
                    }

                    // Select the event
                    long? selectedEventId = await _parent._commandsModule.SelectEventAsync(ctx, eventChannel);
                    if (!selectedEventId.HasValue)
                    {
                        _parent._logger.LogInformation($"User {ctx.User.Id} did not select an event for DM non-signups");
                        return;
                    }

                    // Get users who haven't signed up
                    List<DiscordMember> usersNotSignedUp = await _parent._commandsModule.GetNonSignups(eventChannel.Id, selectedEventId.Value);

                    if (usersNotSignedUp.Count == 0)
                    {
                        await ctx.FollowupAsync(new DiscordWebhookBuilder()
                            .WithContent($"✅ All users in {eventChannel.Mention} have signed up for the event."));

                        _parent._logger.LogInformation($"All users signed up for event {selectedEventId.Value}, no DMs needed");
                        return;
                    }

                    // Format confirmation message
                    var userNames = usersNotSignedUp.Select(user => user.DisplayName).ToList();

                    var embed = new DiscordEmbedBuilder()
                        .WithTitle("Confirm Message Send")
                        .WithDescription($"You are about to send a DM to {usersNotSignedUp.Count} users who haven't signed up.")
                        .AddField("Recipients", string.Join(", ", userNames.Take(25)) + (userNames.Count > 25 ? $" and {userNames.Count - 25} more..." : ""), false)
                        .AddField("Message Content", message, false)
                        .WithColor(DiscordColor.Orange)
                        .WithFooter("Please confirm or cancel this action");

                    // Add confirmation buttons
                    var confirmButton = new DiscordButtonComponent(DiscordButtonStyle.Success, "confirm_notify", "Send Messages");
                    var cancelButton = new DiscordButtonComponent(DiscordButtonStyle.Danger, "cancel_notify", "Cancel");

                    var messageBuilder = new DiscordWebhookBuilder()
                        .AddEmbed(embed)
                        .AddComponents(confirmButton, cancelButton);

                    // Wait for button press
                    var responseMessage = await ctx.FollowupAsync(messageBuilder);
                    var buttonResult = await responseMessage.WaitForButtonAsync(ctx.User, TimeSpan.FromMinutes(2));

                    if (buttonResult.TimedOut)
                    {
                        await ctx.EditFollowupAsync(responseMessage.Id, new DiscordWebhookBuilder()
                            .WithContent("You took too long to respond. Please try again."));

                        _parent._logger.LogInformation($"Button selection timed out for user {ctx.User.Id} for DM non-signups");
                        return;
                    }

                    if (buttonResult.Result.Id == "confirm_notify")
                    {
                        await ctx.EditFollowupAsync(responseMessage.Id, new DiscordWebhookBuilder()
                            .WithContent($"Sending DMs to {usersNotSignedUp.Count} users... This may take a moment."));

                        var failedUsers = new List<string>();
                        var sentCount = 0;

                        // Send DMs to each user
                        foreach (var user in usersNotSignedUp)
                        {
                            try
                            {
                                if (!user.IsBot)
                                {
                                    await user.SendMessageAsync(message);
                                    sentCount++;

                                    // Add a small delay to avoid rate limits
                                    await Task.Delay(200);
                                }
                            }
                            catch (Exception ex)
                            {
                                _parent._logger.LogWarning(ex, $"Failed to send DM to user {user.Id} ({user.Username})");
                                failedUsers.Add(user.Username);
                            }
                        }

                        // Format final response
                        var finalEmbed = new DiscordEmbedBuilder()
                            .WithTitle("DM Operation Complete")
                            .WithDescription($"Successfully sent messages to {sentCount} out of {usersNotSignedUp.Count} users.")
                            .WithColor(failedUsers.Count > 0 ? DiscordColor.Yellow : DiscordColor.Green)
                            .WithTimestamp(DateTime.Now);

                        if (failedUsers.Count > 0)
                        {
                            finalEmbed.AddField("Failed Recipients", string.Join(", ", failedUsers), false);
                            finalEmbed.WithFooter("Users may have DMs disabled or privacy settings that prevented message delivery");
                        }

                        await ctx.EditFollowupAsync(responseMessage.Id, new DiscordWebhookBuilder().AddEmbed(finalEmbed));

                        _parent._logger.LogInformation($"User {ctx.User.Id} sent DMs to {sentCount} users with {failedUsers.Count} failures");
                    }
                    else if (buttonResult.Result.Id == "cancel_notify")
                    {
                        await ctx.EditFollowupAsync(responseMessage.Id, new DiscordWebhookBuilder()
                            .WithContent("Operation canceled. No messages were sent."));

                        _parent._logger.LogInformation($"User {ctx.User.Id} canceled sending DMs to non-signups");
                    }
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error notifying non-signups for user {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while sending notifications. Please try again later.");
                }
            }
        }



        [Command("role")]
        public class RoleCommand
        {
            private readonly AdminCommands _parent;

            public RoleCommand(AdminCommands parent)
            {
                _parent = parent;
            }

            [Command("add"), Description("Add a reaction role to a message")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task AddReactionRole(
            CommandContext ctx,
            [Description("Channel containing the message")] DiscordChannel channel,
            [Description("Message ID to add reaction to")] string messageId,
            [Description("Emoji to use (custom or Unicode)")] string emoji,
            [Description("Role to assign when reacted")] DiscordRole role)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} attempting to add reaction role with emoji {emoji} for role {role.Id} on message {messageId}");

                    // Validate channel
                    if (channel == null || (channel.Type != DiscordChannelType.Text && channel.Type != DiscordChannelType.PublicThread))
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid Channel")
                            .WithDescription("Please provide a valid text channel or thread.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid channel for reaction role");
                        return;
                    }

                    // Parse message ID
                    if (!ulong.TryParse(messageId, out ulong msgId))
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid Message ID")
                            .WithDescription("Please provide a valid message ID (right-click message → Copy ID).")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid message ID: {messageId}");
                        return;
                    }

                    // Fetch the message
                    DiscordMessage message;
                    try
                    {
                        message = await channel.GetMessageAsync(msgId);
                    }
                    catch (Exception ex)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Message Not Found")
                            .WithDescription("The specified message could not be found. Please check the message ID and channel.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning(ex, $"User {ctx.User.Id} provided message ID that couldn't be retrieved: {messageId}");
                        return;
                    }

                    // Parse emoji (could be custom or Unicode)
                    DiscordEmoji discordEmoji;
                    try
                    {
                        var emojiMatch = Regex.Match(emoji, @"<(a)?:([a-zA-Z0-9_]+):(\d+)>");
                        if (emojiMatch.Success)
                        {
                            string name = emojiMatch.Groups[2].Value;
                            ulong id = ulong.Parse(emojiMatch.Groups[3].Value);
                            bool isAnimated = emojiMatch.Groups[1].Success;

                            // Try to get the emoji directly from the guild
                            discordEmoji = ctx.Guild.Emojis.Values.FirstOrDefault(e => e.Id == id);

                            // If not found in guild, create a partial emoji object
                            if (discordEmoji == null)
                            {
                                discordEmoji = DiscordEmoji.FromGuildEmote(ctx.Client, id);
                            }
                        }
                        else
                        {
                            // Try as Unicode emoji
                            discordEmoji = DiscordEmoji.FromUnicode(ctx.Client, emoji);
                        }

                        if (discordEmoji == null)
                        {
                            await ctx.RespondAsync(new DiscordEmbedBuilder()
                                .WithTitle("Invalid Emoji")
                                .WithDescription("The specified emoji could not be found. Use a standard emoji or a custom emoji from this server.")
                                .WithColor(DiscordColor.Red));

                            _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid emoji: {emoji}");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid Emoji")
                            .WithDescription("Could not parse the provided emoji. Please try again with a different emoji.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogError(ex, $"Error parsing emoji '{emoji}' provided by user {ctx.User.Id}");
                        return;
                    }

                    // Validate role
                    if (role == null)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid Role")
                            .WithDescription("Please provide a valid role.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid role");
                        return;
                    }

                    // Check if the bot can manage this role
                    DiscordMember botMember = await ctx.Guild.GetMemberAsync(ctx.Client.CurrentUser.Id);
                    if (role.Position >= botMember.Hierarchy)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Role Hierarchy Issue")
                            .WithDescription($"I cannot assign the role {role.Mention} because it is higher than or equal to my highest role.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} tried to create a reaction role with a role ({role.Id}) higher than the bot's highest role");
                        return;
                    }

                    bool success = await _parent._commandsDAL.SaveReactionRole(
                        (long)channel.Id,
                        (long)message.Id,
                        discordEmoji.Name,
                        discordEmoji.Id != 0 ? (long)discordEmoji.Id : 0,
                        (long)role.Id);

                    if (!success)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Database Error")
                            .WithDescription("Failed to save the reaction role configuration. Please try again later.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogError($"Failed to save reaction role to database for user {ctx.User.Id}");
                        return;
                    }

                    // Add the reaction to the message
                    try
                    {
                        await message.CreateReactionAsync(discordEmoji);
                    }
                    catch (Exception ex)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Reaction Failed")
                            .WithDescription("Failed to add reaction to the message. The role assignment will still work if users add the reaction manually.")
                            .WithColor(DiscordColor.Yellow));

                        _parent._logger.LogError(ex, $"Failed to add reaction {discordEmoji.Name} to message {message.Id} for user {ctx.User.Id}");
                        // Continue anyway as the role assignment will still work
                    }

                    // Respond with success message
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle("Reaction Role Added")
                        .WithDescription($"Successfully configured reaction role for message in {channel.Mention}")
                        .AddField("Emoji", discordEmoji.ToString(), true)
                        .AddField("Role", role.Mention, true)
                        .AddField("Message Link", $"[Jump to Message]({message.JumpLink})")
                        .WithColor(DiscordColor.Green)
                        .WithTimestamp(DateTime.Now);

                    await ctx.RespondAsync(embed);

                    _parent._logger.LogInformation($"User {ctx.User.Id} successfully added reaction role {role.Id} with emoji {discordEmoji.Name} to message {message.Id}");
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error adding reaction role for user {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while adding the reaction role. Please try again later.");
                }
            }

            [Command("remove"), Description("Remove a reaction role from a message")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task RemoveReactionRole(
                CommandContext ctx,
                [Description("Channel containing the message")] DiscordChannel channel,
                [Description("Message ID to remove reaction from")] string messageId,
                [Description("Emoji to remove")] string emoji)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} attempting to remove reaction role with emoji {emoji} from message {messageId}");

                    // Validate channel
                    if (channel == null || (channel.Type != DiscordChannelType.Text && channel.Type != DiscordChannelType.PublicThread))
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid Channel")
                            .WithDescription("Please provide a valid text channel or thread.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid channel for reaction role removal");
                        return;
                    }

                    // Parse message ID
                    if (!ulong.TryParse(messageId, out ulong msgId))
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid Message ID")
                            .WithDescription("Please provide a valid message ID (right-click message → Copy ID).")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid message ID: {messageId}");
                        return;
                    }

                    // Fetch the message
                    DiscordMessage message;
                    try
                    {
                        message = await channel.GetMessageAsync(msgId);
                    }
                    catch (Exception ex)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Message Not Found")
                            .WithDescription("The specified message could not be found. Please check the message ID and channel.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning(ex, $"User {ctx.User.Id} provided message ID that couldn't be retrieved: {messageId}");
                        return;
                    }

                    // Parse emoji (could be custom or Unicode)
                    DiscordEmoji discordEmoji;
                    try
                    {
                        var emojiMatch = Regex.Match(emoji, @"<(a)?:([a-zA-Z0-9_]+):(\d+)>");
                        if (emojiMatch.Success)
                        {
                            string name = emojiMatch.Groups[2].Value;
                            ulong id = ulong.Parse(emojiMatch.Groups[3].Value);
                            bool isAnimated = emojiMatch.Groups[1].Success;

                            // Try to get the emoji directly from the guild
                            discordEmoji = ctx.Guild.Emojis.Values.FirstOrDefault(e => e.Id == id);

                            // If not found in guild, create a partial emoji object
                            if (discordEmoji == null)
                            {
                                discordEmoji = DiscordEmoji.FromGuildEmote(ctx.Client, id);
                            }
                        }
                        else
                        {
                            // Try as Unicode emoji
                            discordEmoji = DiscordEmoji.FromUnicode(ctx.Client, emoji);
                        }

                        if (discordEmoji == null)
                        {
                            await ctx.RespondAsync(new DiscordEmbedBuilder()
                                .WithTitle("Invalid Emoji")
                                .WithDescription("The specified emoji could not be found. Use a standard emoji or a custom emoji from this server.")
                                .WithColor(DiscordColor.Red));

                            _parent._logger.LogWarning($"User {ctx.User.Id} provided invalid emoji: {emoji}");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Invalid Emoji")
                            .WithDescription("Could not parse the provided emoji. Please try again with a different emoji.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogError(ex, $"Error parsing emoji '{emoji}' provided by user {ctx.User.Id}");
                        return;
                    }

                    // Get role information before removing
                    string roleName = "Unknown Role";
                    var reactionRole = await _parent._commandsDAL.GetReactionRole(
                        (long)channel.Id,
                        (long)message.Id,
                        discordEmoji.Name,
                        discordEmoji.Id != 0 ? (long)discordEmoji.Id : 0);

                    if (reactionRole != null && reactionRole.RoleId > 0)
                    {
                        try
                        {
                            var role = await ctx.Guild.GetRoleAsync((ulong)reactionRole.RoleId);
                            if (role != null)
                            {
                                roleName = role.Name;
                            }
                        }
                        catch
                        {
                            // Role might have been deleted
                        }
                    }

                    // Remove from database
                    bool success = await _parent._commandsDAL.RemoveReactionRole(
                        (long)channel.Id,
                        (long)message.Id,
                        discordEmoji.Name,
                        discordEmoji.Id != 0 ? (long)discordEmoji.Id : 0);

                    if (!success)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("Removal Failed")
                            .WithDescription("This reaction role doesn't exist or couldn't be removed from the database.")
                            .WithColor(DiscordColor.Red));

                        _parent._logger.LogWarning($"User {ctx.User.Id} failed to remove reaction role - not found in database");
                        return;
                    }

                    // Try to remove the reaction from the message (all instances)
                    try
                    {
                        await message.DeleteAllReactionsAsync(discordEmoji);
                    }
                    catch (Exception ex)
                    {
                        _parent._logger.LogWarning(ex, $"Failed to remove emoji reactions from message {message.Id} for user {ctx.User.Id}");
                        // Continue anyway as we've removed it from the database
                    }

                    // Respond with success
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle("Reaction Role Removed")
                        .WithDescription($"Successfully removed reaction role from message in {channel.Mention}")
                        .AddField("Emoji", discordEmoji.ToString(), true)
                        .AddField("Previously Assigned Role", roleName, true)
                        .AddField("Message Link", $"[Jump to Message]({message.JumpLink})")
                        .WithColor(DiscordColor.Orange)
                        .WithTimestamp(DateTime.Now);

                    await ctx.RespondAsync(embed);

                    _parent._logger.LogInformation($"User {ctx.User.Id} successfully removed reaction role with emoji {discordEmoji.Name} from message {message.Id}");
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error removing reaction role for user {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while removing the reaction role. Please try again later.");
                }
            }

            [Command("list"), Description("List all reaction roles in the server")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task ListReactionRoles(CommandContext ctx)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} requesting reaction roles list");

                    var reactionRoles = await _parent._commandsDAL.GetAllReactionRoles();

                    if (reactionRoles == null || reactionRoles.Count == 0)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("No Reaction Roles")
                            .WithDescription("There are no reaction roles set up in this server.")
                            .WithColor(DiscordColor.Yellow));

                        _parent._logger.LogInformation($"No reaction roles found for user {ctx.User.Id}");
                        return;
                    }

                    // Build response with pagination if needed
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle("Reaction Roles")
                        .WithDescription($"There are {reactionRoles.Count} reaction roles set up in this server.")
                        .WithColor(DiscordColor.Blurple)
                        .WithTimestamp(DateTime.Now);

                    int count = 0;
                    foreach (var rr in reactionRoles)
                    {
                        if (count >= 25) // Discord limits fields to 25
                        {
                            embed.WithFooter($"Showing 25/{reactionRoles.Count} reaction roles");
                            break;
                        }

                        // Get channel, message and role
                        string channelMention = $"<#{rr.ChannelId}>";
                        string roleMention = $"<@&{rr.RoleId}>";

                        // Format emoji
                        string emojiDisplay;
                        if (rr.EmojiId > 0)
                        {
                            emojiDisplay = $"<:{rr.EmojiName}:{rr.EmojiId}>";
                        }
                        else
                        {
                            emojiDisplay = rr.EmojiName;
                        }

                        string messageLink = $"https://discord.com/channels/{ctx.Guild.Id}/{rr.ChannelId}/{rr.MessageId}";

                        embed.AddField(
                            $"Reaction Role {count + 1}",
                            $"Emoji: {emojiDisplay}\nRole: {roleMention}\nChannel: {channelMention}\n[Jump to Message]({messageLink})",
                            true
                        );

                        count++;
                    }

                    await ctx.RespondAsync(embed);

                    _parent._logger.LogInformation($"Successfully displayed {count} reaction roles for user {ctx.User.Id}");
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error listing reaction roles for user {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while retrieving reaction roles. Please try again later.");
                }
            }
        }

        [Command("autorole")]
        public class AutoRoleCommands
        {
            private readonly AdminCommands _parent;

            public AutoRoleCommands(AdminCommands parent)
            {
                _parent = parent;
            }

            [Command("add"), Description("Add a role to be automatically assigned to new members")]
            [RequirePermissions(DiscordPermissions.ManageRoles)]
            public async Task AddAutoRole(
                CommandContext ctx,
                [Description("Role to automatically assign")] DiscordRole role)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} attempting to add auto-role {role.Name} ({role.Id})");

                    await ctx.DeferResponseAsync();

                    // Add to database
                    var success = await _parent._autoRoleDAL.AddAutoRoleAsync((long)role.Id, role.Name);

                    if (!success)
                    {
                        var errorEmbed = new DiscordEmbedBuilder()
                            .WithTitle("❌ Auto-Role Already Exists")
                            .WithDescription($"The role {role.Mention} is already configured as an auto-role.")
                            .WithColor(DiscordColor.Red)
                            .WithTimestamp(DateTime.Now);

                        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(errorEmbed));
                        return;
                    }

                    var successEmbed = new DiscordEmbedBuilder()
                        .WithTitle("✅ Auto-Role Added")
                        .WithDescription($"The role {role.Mention} will now be automatically assigned to all new members.")
                        .AddField("Role Name", role.Name, true)
                        .AddField("Role ID", role.Id.ToString(), true)
                        .WithColor(DiscordColor.Green)
                        .WithTimestamp(DateTime.Now);

                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(successEmbed));

                    _parent._logger.LogInformation($"Successfully added auto-role {role.Name} ({role.Id})");
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error adding auto-role {role.Id}");
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        "An error occurred while adding the auto-role. Please try again later."));
                }
            }

            [Command("remove"), Description("Remove a role from auto-assignment")]
            [RequirePermissions(DiscordPermissions.ManageRoles)]
            public async Task RemoveAutoRole(
                CommandContext ctx,
                [Description("Role to remove from auto-assignment")] DiscordRole role)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} attempting to remove auto-role {role.Name} ({role.Id})");

                    await ctx.DeferResponseAsync();

                    // Remove from database
                    var success = await _parent._autoRoleDAL.RemoveAutoRoleAsync((long)role.Id);

                    if (!success)
                    {
                        var errorEmbed = new DiscordEmbedBuilder()
                            .WithTitle("❌ Auto-Role Not Found")
                            .WithDescription($"The role {role.Mention} is not configured as an auto-role.")
                            .WithColor(DiscordColor.Red)
                            .WithTimestamp(DateTime.Now);

                        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(errorEmbed));
                        return;
                    }

                    var successEmbed = new DiscordEmbedBuilder()
                        .WithTitle("✅ Auto-Role Removed")
                        .WithDescription($"The role {role.Mention} will no longer be automatically assigned to new members.")
                        .AddField("Role Name", role.Name, true)
                        .AddField("Role ID", role.Id.ToString(), true)
                        .WithColor(DiscordColor.Orange)
                        .WithTimestamp(DateTime.Now);

                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(successEmbed));

                    _parent._logger.LogInformation($"Successfully removed auto-role {role.Name} ({role.Id})");
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error removing auto-role {role.Id}");
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        "An error occurred while removing the auto-role. Please try again later."));
                }
            }

            [Command("list"), Description("List all roles that are automatically assigned to new members")]
            [RequirePermissions(DiscordPermissions.ManageRoles)]
            public async Task ListAutoRoles(CommandContext ctx)
            {
                try
                {
                    _parent._logger.LogInformation($"User {ctx.User.Id} requesting auto-role list");

                    await ctx.DeferResponseAsync();

                    // Get all auto-roles
                    var autoRoles = await _parent._autoRoleDAL.GetAllAutoRolesAsync();

                    if (autoRoles == null || autoRoles.Count == 0)
                    {
                        var emptyEmbed = new DiscordEmbedBuilder()
                            .WithTitle("📋 Auto-Roles")
                            .WithDescription("No auto-roles are currently configured.\n\nUse `/admin autorole add` to add roles that will be automatically assigned to new members.")
                            .WithColor(DiscordColor.Blue)
                            .WithTimestamp(DateTime.Now);

                        await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(emptyEmbed));
                        return;
                    }

                    var embed = new DiscordEmbedBuilder()
                        .WithTitle("📋 Auto-Roles")
                        .WithDescription($"The following {autoRoles.Count} role(s) are automatically assigned to new members:")
                        .WithColor(DiscordColor.Blue)
                        .WithTimestamp(DateTime.Now);

                    var guild = ctx.Guild;
                    var validRoles = new List<string>();
                    var invalidRoles = new List<string>();

                    foreach (var autoRole in autoRoles)
                    {
                        try
                        {
                            var role = await guild.GetRoleAsync((ulong)autoRole.RoleId);
                            if (role != null)
                            {
                                validRoles.Add($"{role.Mention} - `{role.Name}` (ID: {role.Id})");
                            }
                            else
                            {
                                invalidRoles.Add($"⚠️ **{autoRole.RoleName}** (ID: {autoRole.RoleId}) - Role not found in server");
                            }
                        }
                        catch
                        {
                            invalidRoles.Add($"⚠️ **{autoRole.RoleName}** (ID: {autoRole.RoleId}) - Error accessing role");
                        }
                    }

                    if (validRoles.Count > 0)
                    {
                        embed.AddField("Active Auto-Roles", string.Join("\n", validRoles), false);
                    }

                    if (invalidRoles.Count > 0)
                    {
                        embed.AddField("⚠️ Invalid Roles", string.Join("\n", invalidRoles), false);
                        embed.WithFooter("Invalid roles should be removed using /admin autorole remove");
                    }

                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));

                    _parent._logger.LogInformation($"Listed {autoRoles.Count} auto-roles for user {ctx.User.Id}");
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, "Error listing auto-roles");
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        "An error occurred while retrieving auto-roles. Please try again later."));
                }
            }
        }

        /// <summary>
        /// Interview timer management commands
        /// </summary>
        [Command("interview")]
        public class InterviewCommands
        {
            private readonly AdminCommands _parent;

            public InterviewCommands(AdminCommands parent)
            {
                _parent = parent;
            }

            /// <summary>
            /// Shows timer control buttons (ephemeral - only visible to you)
            /// </summary>
            [Command("control"), Description("Show timer control buttons for this interview (only you can see them)")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task Control(CommandContext ctx)
            {
                try
                {
                    var channel = ctx.Channel;

                    // Check if this is an interview channel
                    if (!channel.Name.EndsWith("-interview"))
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("❌ Not an Interview Channel")
                            .WithDescription("This command can only be used in interview channels.")
                            .WithColor(DiscordColor.Red)
                            .Build());
                        return;
                    }

                    _parent._logger.LogInformation($"Admin {ctx.User.Id} requested timer controls for channel {channel.Id}");

                    // Create control buttons
                    var pauseButton = new DiscordButtonComponent(
                        DiscordButtonStyle.Secondary,
                        $"timer_pause_{channel.Id}",
                        "⏸️ Pause Timer",
                        false);

                    var extendButton = new DiscordButtonComponent(
                        DiscordButtonStyle.Primary,
                        $"timer_extend_{channel.Id}",
                        "⏱️ Extend +24h",
                        false);

                    var resumeButton = new DiscordButtonComponent(
                        DiscordButtonStyle.Success,
                        $"timer_resume_{channel.Id}",
                        "▶️ Resume Timer",
                        false);

                    var embed = new DiscordEmbedBuilder()
                        .WithTitle("⏰ Interview Timer Controls")
                        .WithDescription("Use these buttons to manage the follow-up timer:\n\n" +
                                       "• **⏸️ Pause Timer** - Stop the timer indefinitely (e.g., applicant said they'll be away)\n" +
                                       "• **⏱️ Extend +24h** - Give the applicant 24 more hours from now\n" +
                                       "• **▶️ Resume Timer** - Resume a paused timer (restarts when you send next message)")
                        .WithColor(DiscordColor.Blurple)
                        .WithFooter("Only you can see this message");

                    var builder = new DiscordInteractionResponseBuilder()
                        .AddEmbed(embed)
                        .AddComponents(pauseButton, extendButton, resumeButton)
                        .AsEphemeral(true); // THIS MAKES IT ONLY VISIBLE TO THE ADMIN!

                    await ctx.RespondAsync(builder);

                    _parent._logger.LogInformation($"Sent ephemeral timer controls to admin {ctx.User.Id}");
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error showing timer controls for user {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while retrieving timer controls. Please try again later.");
                }
            }

            /// <summary>
            /// Check the current status of the interview timer
            /// </summary>
            [Command("status"), Description("Check the status of the timer for this interview")]
            [RequirePermissions(DiscordPermissions.ModerateMembers)]
            public async Task Status(CommandContext ctx)
            {
                try
                {
                    var channel = ctx.Channel;

                    // Check if this is an interview channel
                    if (!channel.Name.EndsWith("-interview"))
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("❌ Not an Interview Channel")
                            .WithDescription("This command can only be used in interview channels.")
                            .WithColor(DiscordColor.Red)
                            .Build());
                        return;
                    }

                    // Get the service through the parent's access
                    var followUpService = _parent._interviewFollowUpService;
                    if (followUpService == null)
                    {
                        await ctx.RespondAsync("❌ Interview follow-up service is not available.");
                        return;
                    }

                    var timer = followUpService.GetTimerInfo(channel.Id);

                    if (timer == null)
                    {
                        await ctx.RespondAsync(new DiscordEmbedBuilder()
                            .WithTitle("ℹ️ No Active Timer")
                            .WithDescription("There is no active follow-up timer for this interview channel.")
                            .WithColor(DiscordColor.Gray)
                            .Build());
                        return;
                    }

                    var elapsed = DateTimeOffset.UtcNow - timer.StartTime;
                    var timeRemaining = TimeSpan.FromHours(24) - elapsed;

                    var statusEmbed = new DiscordEmbedBuilder()
                        .WithTitle("⏰ Timer Status")
                        .AddField("Stage", timer.Stage.ToString(), true)
                        .AddField("State", timer.IsPaused ? "⏸️ Paused" : "▶️ Running", true)
                        .AddField("Started", $"<t:{timer.StartTime.ToUnixTimeSeconds()}:R>", false)
                        .WithColor(timer.IsPaused ? DiscordColor.Orange : DiscordColor.Green);

                    if (!timer.IsPaused && timer.Stage == TimerStage.FirstWarning)
                    {
                        if (timeRemaining.TotalMinutes > 0)
                        {
                            statusEmbed.AddField("Time Until Warning",
                                $"{(int)timeRemaining.TotalHours}h {timeRemaining.Minutes}m", false);
                        }
                        else
                        {
                            statusEmbed.AddField("Warning", "Due to be sent soon", false);
                        }
                    }
                    else if (!timer.IsPaused && timer.Stage == TimerStage.FinalWarning && timer.FirstWarningSentAt.HasValue)
                    {
                        var finalElapsed = DateTimeOffset.UtcNow - timer.FirstWarningSentAt.Value;
                        var finalRemaining = TimeSpan.FromHours(24) - finalElapsed;

                        if (finalRemaining.TotalMinutes > 0)
                        {
                            statusEmbed.AddField("Time Until Closure",
                                $"{(int)finalRemaining.TotalHours}h {finalRemaining.Minutes}m", false);
                        }
                        else
                        {
                            statusEmbed.AddField("Closure", "Due to happen soon", false);
                        }
                    }

                    await ctx.RespondAsync(statusEmbed.Build());
                }
                catch (Exception ex)
                {
                    _parent._logger.LogError(ex, $"Error checking timer status for user {ctx.User.Id}");
                    await ctx.RespondAsync("An error occurred while checking timer status. Please try again later.");
                }
            }
        }
    }
}
