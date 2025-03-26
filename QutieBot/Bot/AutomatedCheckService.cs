using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QutieBot.Bot;
using QutieBot.Bot.Commands;
using QutieDAL.DAL;
using QutieDTO.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QutieBot.Bot.Services
{
    public class AutomatedCheckService
    {
        private readonly RaidHelperManager _raidHelperManager;
        private readonly CommandsModule _commandsModule;
        private readonly DiscordClient _discordClient;
        private readonly AutomatedCheckDAL _automatedCheckDAL;
        private readonly ILogger<AutomatedCheckService> _logger;

        private readonly ConcurrentDictionary<string, Task> _scheduledChecks = new ConcurrentDictionary<string, Task>();

        public AutomatedCheckService(
            RaidHelperManager raidHelperManager,
            CommandsModule commandsModule,
            DiscordClient discordClient,
            AutomatedCheckDAL automatedCheckDAL,
            ILogger<AutomatedCheckService> logger)
        {
            _raidHelperManager = raidHelperManager ?? throw new ArgumentNullException(nameof(raidHelperManager));
            _commandsModule = commandsModule ?? throw new ArgumentNullException(nameof(commandsModule));
            _discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
            _automatedCheckDAL = automatedCheckDAL ?? throw new ArgumentNullException(nameof(automatedCheckDAL));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task MonitorEvents(CancellationToken stoppingToken = default)
        {
            // Get all current events
            var events = await _raidHelperManager.GetEventsFromDb();

            if (events == null || !events.Any())
            {
                return;
            }

            // Get all automated check configurations
            var checkConfigs = await _automatedCheckDAL.GetAllAutomatedChecks();

            if (checkConfigs == null || !checkConfigs.Any())
            {
                return;
            }

            // Get current date
            var today = DateTime.UtcNow.Date;

            // Find today's events 
            var todaysEvents = events.Where(e => e.Date.Date == today).ToList();

            foreach (var eventItem in todaysEvents)
            {
                foreach (var checkConfig in checkConfigs)
                {
                    // Create a unique key for this event-check combination
                    string checkKey = $"{eventItem.EventId}_{checkConfig.Id}";

                    // Check if this specific check for this event is already scheduled or processed
                    if (_scheduledChecks.ContainsKey(checkKey))
                    {
                        continue;
                    }

                    // Check if the event has already been processed by this check configuration
                    bool alreadyProcessed = await _automatedCheckDAL.HasEventBeenProcessed(eventItem.EventId, checkConfig.Id);
                    if (alreadyProcessed)
                    {
                        continue;
                    }

                    _logger.LogInformation("Scheduling check {CheckId} with {Delay} minute delay for event {EventId}",
                        checkConfig.Id, checkConfig.CheckDelayMinutes, eventItem.EventId);

                    // Schedule the check
                    var checkTask = ScheduleAttendanceCheck(eventItem, checkConfig, stoppingToken);
                    _scheduledChecks.TryAdd(checkKey, checkTask);
                }
            }

            // Clean up completed checks
            foreach (var scheduledCheck in _scheduledChecks.Where(sc =>
                sc.Value.IsCompleted).ToList())
            {
                _scheduledChecks.TryRemove(scheduledCheck.Key, out _);
            }
        }

        private async Task ScheduleAttendanceCheck(Event eventItem, AutomatedChecks checkConfig, CancellationToken stoppingToken)
        {
            try
            {
                // Calculate the delay - if the event already started, adjust the delay accordingly
                var eventStartTime = eventItem.Date; // This might need adjustment based on your actual event date/time model
                var currentTime = DateTime.UtcNow;
                var targetCheckTime = eventStartTime.AddMinutes(checkConfig.CheckDelayMinutes);

                TimeSpan delay;
                if (targetCheckTime > currentTime)
                {
                    delay = targetCheckTime - currentTime;
                }
                else
                {
                    delay = TimeSpan.FromSeconds(5);
                }

                // Wait for the calculated delay
                await Task.Delay(delay, stoppingToken);

                _logger.LogInformation("Performing automated check {CheckId} for event {EventId}",
                    checkConfig.Id, eventItem.EventId);

                // Get channel information
                var channelId = eventItem.ChannelId;
                if (channelId == 0)
                {
                    _logger.LogWarning("Event {EventId} has no valid channel ID", eventItem.EventId);
                    return;
                }

                // Find the appropriate voice channel for this event
                var voiceChannelId = await GetVoiceChannelForEvent(eventItem);
                if (voiceChannelId == 0)
                {
                    _logger.LogWarning("Could not determine voice channel for event {EventId}", eventItem.EventId);
                    return;
                }

                // Perform the attendance check
                await PerformAttendanceCheck(eventItem.EventId, channelId, voiceChannelId, checkConfig);

                // Mark the event as processed by this check
                await _automatedCheckDAL.MarkEventAsProcessed(eventItem.EventId, checkConfig.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing scheduled check {CheckId} for event {EventId}",
                    checkConfig.Id, eventItem.EventId);
            }
        }

        private async Task<ulong> GetVoiceChannelForEvent(Event eventItem)
        {
            try
            {
                DiscordChannel eventChannel = await _discordClient.GetChannelAsync((ulong)eventItem.ChannelId);
                if (eventChannel == null)
                {
                    _logger.LogWarning("Event channel {ChannelId} not found", eventItem.ChannelId);
                    return 0;
                }

                // Find voice channels in the same category
                if (eventChannel.Parent != null)
                {
                    var category = eventChannel.Parent;
                    var voiceChannels = category.Children
                        .Where(c => c.Type == DiscordChannelType.Voice || c.Type == DiscordChannelType.Stage)
                        .ToList();

                    if (voiceChannels.Any())
                    {
                        // Select the voice channel with the most members
                        var channelWithMostMembers = voiceChannels
                            .OrderByDescending(c => c.Users.Count)
                            .FirstOrDefault();

                        if (channelWithMostMembers != null && channelWithMostMembers.Users.Count != 0)
                        {
                            _logger.LogInformation("Selected voice channel {ChannelName} with {MemberCount} members for event {EventId}",
                                channelWithMostMembers.Name, channelWithMostMembers.Users.Count, eventItem.EventId);
                            return channelWithMostMembers.Id;
                        }
                    }
                }

                var guild = eventChannel.Guild;
                var allVoiceChannels = guild.Channels.Values
                    .Where(c => (c.Type == DiscordChannelType.Voice || c.Type == DiscordChannelType.Stage) && c.Users.Any())
                    .ToList();

                if (allVoiceChannels.Any())
                {
                    var mostPopulatedChannel = allVoiceChannels
                        .OrderByDescending(c => c.Users.Count)
                        .First();

                    _logger.LogInformation("Selected guild voice channel {ChannelName} with {MemberCount} members for event {EventId}",
                        mostPopulatedChannel.Name, mostPopulatedChannel.Users.Count, eventItem.EventId);
                    return mostPopulatedChannel.Id;
                }

                _logger.LogWarning("No suitable voice channel found for event {EventId}", eventItem.EventId);
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error determining voice channel for event {EventId}", eventItem.EventId);
                return 0;
            }
        }

        private async Task PerformAttendanceCheck(long eventId, long eventChannelId, ulong voiceChannelId, AutomatedChecks checkConfig)
        {
            try
            {
                // Get users who signed up but aren't in voice
                List<SignUpData> absentUsers = await _commandsModule.GetUsersNotInVoiceChannel(
                    voiceChannelId, eventId);

                var eventChannel = await _discordClient.GetChannelAsync((ulong)eventChannelId);

                if (absentUsers.Count == 0)
                {
                    _logger.LogInformation("Automated check complete: All attendees present for event {EventId}", eventId);
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle("✅ Attendance Check Complete")
                        .WithDescription("All attendees are present! 🎉")
                        .WithColor(DiscordColor.Green)
                        .WithTimestamp(DateTimeOffset.Now);

                    await eventChannel.SendMessageAsync(embed);
                    return;
                }

                _logger.LogInformation("Found {Count} absent users for event {EventId}", absentUsers.Count, eventId);

                // Separate late users from absent users
                var lateUsers = absentUsers.Where(u =>
                    u.specName == "Late" || u.className == "Late" || u.roleName == "Late").ToList();

                var trulyAbsentUsers = absentUsers.Except(lateUsers).ToList();

                // Send notification to the event channel
                if (eventChannel != null)
                {
                    // Build the notification message
                    var message = new StringBuilder();
                    message.AppendLine($"**⚠️ Automated Attendance Check ({checkConfig.CheckDelayMinutes} minutes after start)**");

                    if (trulyAbsentUsers.Any())
                    {
                        message.AppendLine($"\n__**Missing Attendees ({trulyAbsentUsers.Count})**__");

                        foreach (var user in trulyAbsentUsers)
                        {
                            string userMention = checkConfig.PingUsers ? $"<@{user.userId}>" : user.name;
                            message.AppendLine($"❌ {userMention}");
                        }
                    }

                    if (lateUsers.Any())
                    {
                        message.AppendLine($"\n__**Late Attendees ({lateUsers.Count})**__");

                        foreach (var user in lateUsers)
                        {
                            string userMention = checkConfig.PingUsers ? $"<@{user.userId}>" : user.name;
                            message.AppendLine($"⏰ {userMention}");
                        }
                    }

                    var embed = new DiscordEmbedBuilder()
                        .WithTitle($"Automated Attendance Check")
                        .WithDescription(message.ToString())
                        .WithColor(DiscordColor.Orange)
                        .WithTimestamp(DateTimeOffset.Now);


                    string contentMessage = "";
                    if (checkConfig.PingUsers)
                    {
                        var mentionsBuilder = new StringBuilder();

                        foreach (var user in trulyAbsentUsers)
                        {
                            mentionsBuilder.Append($"<@{user.userId}> ");
                        }

                        foreach (var user in lateUsers)
                        {
                            mentionsBuilder.Append($"<@{user.userId}> ");
                        }

                        contentMessage = mentionsBuilder.ToString().Trim();
                    }


                    var messageBuilder = new DiscordMessageBuilder()
                        .WithContent(contentMessage)
                        .AddEmbed(embed)
                        .WithAllowedMentions(Mentions.All);

                    await eventChannel.SendMessageAsync(messageBuilder);

                    // Auto-remove users if configured
                    var removedAbsentUsers = new List<SignUpData>();
                    var removedLateUsers = new List<SignUpData>();

                    // Remove absent users if enabled
                    if (checkConfig.AutoRemoveAbsentUsers && trulyAbsentUsers.Any())
                    {
                        foreach (var user in trulyAbsentUsers)
                        {
                            var result = await _commandsModule.RemoveAttendance(eventId, (long)user.userId);
                            if (result)
                            {
                                removedAbsentUsers.Add(user);
                            }
                        }
                    }

                    // Remove late users if enabled
                    if (checkConfig.AutoRemoveLateUsers && lateUsers.Any())
                    {
                        foreach (var user in lateUsers)
                        {
                            var result = await _commandsModule.RemoveAttendance(eventId, (long)user.userId);
                            if (result)
                            {
                                removedLateUsers.Add(user);
                            }
                        }
                    }

                    // Update events to refresh data if we removed anyone
                    if (removedAbsentUsers.Any() || removedLateUsers.Any())
                    {
                        // Send a follow-up message about removed users
                        var followUpMessage = new StringBuilder();
                        followUpMessage.AppendLine("**🔄 Attendance Update**");

                        if (removedAbsentUsers.Any())
                        {
                            followUpMessage.AppendLine($"\nRemoved {removedAbsentUsers.Count} absent users:");
                            followUpMessage.AppendLine(string.Join(", ", removedAbsentUsers.Select(u => u.name)));
                        }

                        if (removedLateUsers.Any())
                        {
                            followUpMessage.AppendLine($"\nRemoved {removedLateUsers.Count} late users:");
                            followUpMessage.AppendLine(string.Join(", ", removedLateUsers.Select(u => u.name)));
                        }

                        var followUpEmbed = new DiscordEmbedBuilder()
                            .WithDescription(followUpMessage.ToString())
                            .WithColor(DiscordColor.Red)
                            .WithTimestamp(DateTimeOffset.Now);

                        await eventChannel.SendMessageAsync(followUpEmbed);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing attendance check for event {EventId}", eventId);
            }
        }
    }
}