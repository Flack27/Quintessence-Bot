using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using Microsoft.Extensions.Logging;
using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QutieBot.Bot.Commands
{
    public class CommandsModule
    {
        private readonly RaidHelperManager _bot;
        private readonly DiscordClient _client;
        private readonly ILogger<CommandsModule> _logger;

        public CommandsModule(
            RaidHelperManager bot,
            DiscordClient client,
            ILogger<CommandsModule> logger)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<DiscordMember>> GetNonSignups(ulong channelId, long eventId)
        {
            try
            {
                _logger.LogInformation($"Getting non-signups for event {eventId} in channel {channelId}");

                EventData evt = await _bot.GetRaidHelperEvent(eventId.ToString());

                if (evt == null)
                {
                    _logger.LogWarning($"Event {eventId} not found in RaidHelper");
                    return new List<DiscordMember>();
                }

                DiscordChannel channel = await _client.GetChannelAsync(channelId);

                if (channel == null)
                {
                    _logger.LogWarning($"Channel {channelId} not found");
                    return new List<DiscordMember>();
                }

                DiscordGuild guild = channel.Guild;

                _logger.LogDebug($"Getting all members for guild {guild.Name}");

                // Create a list to store members
                List<DiscordMember> guildMembers = new List<DiscordMember>();

                // Get all members in batches for better performance
                await foreach (var member in guild.GetAllMembersAsync())
                {
                    guildMembers.Add(member);
                }

                // Find members with channel access who aren't bots
                var membersInChannel = guildMembers
                    .Where(member => member.PermissionsIn(channel).HasPermission(DiscordPermissions.AccessChannels)
                        && !member.IsBot);

                // Get list of users who are signed up
                List<ulong> signedUpUserIds = (evt.signUps ?? new List<SignUpData>())
                    .Select(signUp => signUp.userId)
                    .ToList();

                // Find members who have access to the channel but haven't signed up
                List<DiscordMember> usersNotSignedUp = membersInChannel
                    .Where(member => !signedUpUserIds.Contains(member.Id) && !member.IsBot)
                    .ToList();

                _logger.LogInformation($"Found {usersNotSignedUp.Count} members who haven't signed up for event {eventId}");

                return usersNotSignedUp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting non-signups for event {eventId} in channel {channelId}");
                return new List<DiscordMember>();
            }
        }

        public async Task<List<DiscordMember>> GetmembersInChannel(ulong channelId)
        {
            try
            {
                _logger.LogInformation($"Getting members with access to channel {channelId}");

                DiscordChannel channel = await _client.GetChannelAsync(channelId);

                if (channel == null)
                {
                    _logger.LogWarning($"Channel {channelId} not found");
                    return new List<DiscordMember>();
                }

                DiscordGuild guild = channel.Guild;

                // Create a list to store members
                List<DiscordMember> guildMembers = new List<DiscordMember>();

                // Get all members in batches for better performance
                await foreach (var member in guild.GetAllMembersAsync())
                {
                    guildMembers.Add(member);
                }

                // Find members with channel access who aren't bots
                var membersInChannel = guildMembers
                    .Where(member => member.PermissionsIn(channel).HasPermission(DiscordPermissions.AccessChannels)
                        && !member.IsBot)
                    .ToList();

                _logger.LogInformation($"Found {membersInChannel.Count} members with access to channel {channelId}");

                return membersInChannel;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting members for channel {channelId}");
                return new List<DiscordMember>();
            }
        }

        public async Task<List<SignUpData>> GetUsersNotInVoiceChannel(ulong voiceChannelId, long eventId)
        {
            try
            {
                _logger.LogInformation($"Getting signed-up users not in voice channel {voiceChannelId} for event {eventId}");

                EventData evt = await _bot.GetRaidHelperEvent(eventId.ToString());

                if (evt == null)
                {
                    _logger.LogWarning($"Event {eventId} not found in RaidHelper");
                    return new List<SignUpData>();
                }

                DiscordChannel channel = await _client.GetChannelAsync(voiceChannelId);

                if (channel == null || (channel.Type != DiscordChannelType.Voice && channel.Type != DiscordChannelType.Stage))
                {
                    _logger.LogWarning($"Voice channel {voiceChannelId} not found or not a voice/stage channel");
                    return new List<SignUpData>();
                }

                // Get users currently in the voice channel
                List<ulong> membersInChannel = channel.Users
                    .Select(member => member.Id)
                    .ToList();

                // Get valid sign-ups (excluding ignored spec names)
                List<SignUpData> signedUpUsers = (evt.signUps ?? new List<SignUpData>())
                    .Where(signup =>
                        !_bot.IsIgnoredSpecName(signup.specName) &&
                        !_bot.IsIgnoredSpecName(signup.className) &&
                        !_bot.IsIgnoredSpecName(signup.roleName))
                    .ToList();

                // Find sign-ups who aren't in the voice channel
                List<SignUpData> usersNotShowedup = signedUpUsers
                    .Where(member => !membersInChannel.Contains(member.userId))
                    .ToList();

                _logger.LogInformation($"Found {usersNotShowedup.Count} signed-up users not in voice channel {voiceChannelId}");

                return usersNotShowedup;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting users not in voice channel {voiceChannelId} for event {eventId}");
                return new List<SignUpData>();
            }
        }

        public async Task<List<Event>> GetEvents(ulong channelId)
        {
            try
            {
                _logger.LogInformation($"Getting events for channel {channelId}");

                List<EventData> events = await _bot.GetRaidHelperEvents(channelId.ToString());

                if (events == null || events.Count == 0)
                {
                    _logger.LogInformation($"No events found for channel {channelId}");
                    return new List<Event>();
                }

                var result = events
                    .Select(e => new Event
                    {
                        EventId = (long)e.id,
                        Title = e.title,
                        Date = DateTimeOffset.FromUnixTimeSeconds(e.startTime).UtcDateTime.Date
                    })
                    .ToList();

                _logger.LogInformation($"Found {result.Count} events for channel {channelId}");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting events for channel {channelId}");
                return new List<Event>();
            }
        }

        public async Task<long?> SelectEventAsync(CommandContext ctx, DiscordChannel targetChannel)
        {
            try
            {
                _logger.LogInformation($"User {ctx.User.Id} selecting an event from channel {targetChannel.Id}");

                List<Event> events = await GetEvents(targetChannel.Id);

                if (events.Count == 0)
                {
                    await ctx.RespondAsync($"No events found in {targetChannel.Mention}.");
                    return null;
                }

                // Sort events by date to show most recent first
                events = events.OrderBy(e => e.Date).ToList();

                // Create a selection dropdown with event details
                var selectMenu = new DiscordSelectComponent(
                    "event_select",
                    "Select an event",
                    events.Select(e => new DiscordSelectComponentOption(
                        $"{e.Title} - {e.Date:yyyy-MM-dd}",
                        e.EventId.ToString()
                    )).ToList()
                );

                // Send the message with the dropdown
                var builder = new DiscordMessageBuilder()
                    .WithContent("Select an event:")
                    .AddComponents(selectMenu);

                await ctx.RespondAsync(builder);
                var responseMessage = await ctx.GetResponseAsync();

                if (responseMessage == null)
                {
                    _logger.LogWarning($"Failed to get response message for event selection");
                    return null;
                }

                // Wait for user to select an option
                var result = await responseMessage.WaitForSelectAsync(
                    ctx.User,
                    "event_select",
                    TimeSpan.FromMinutes(2)
                );

                if (result.TimedOut)
                {
                    _logger.LogInformation($"User {ctx.User.Id} timed out selecting an event");
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                        .WithContent("You took too long to respond. Please try again."));
                    return null;
                }

                if (!long.TryParse(result.Result.Values.First(), out long selectedEventId))
                {
                    _logger.LogWarning($"User {ctx.User.Id} selected invalid event ID");
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                        .WithContent("Invalid event selected. Please try again."));
                    return null;
                }

                var selectedEvent = events.First(e => e.EventId == selectedEventId);
                _logger.LogInformation($"User {ctx.User.Id} selected event {selectedEventId}: {selectedEvent.Title}");

                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"You selected: **{selectedEvent.Title}**."));

                return selectedEventId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error selecting event from channel {targetChannel.Id}");

                // Try to respond with error if possible
                try
                {
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                        .WithContent("An error occurred while selecting an event. Please try again."));
                }
                catch { /* Ignore if this fails too */ }

                return null;
            }
        }

        public async Task<long?> SelectEventFromDb(CommandContext ctx)
        {
            try
            {
                _logger.LogInformation($"User {ctx.User.Id} selecting an event from database");

                List<Event> events = await _bot.GetEventsFromDb();
                events = events.Where(e => e.Date <= DateTime.UtcNow).ToList();

                if (events.Count == 0)
                {
                    await ctx.RespondAsync($"No events found in the last 7 days.");
                    return null;
                }

                // Sort events by date to show most recent first
                events = events.OrderByDescending(e => e.Date).ToList();

                // Create a selection dropdown with event details
                var selectMenu = new DiscordSelectComponent(
                    "event_select",
                    "Select an event",
                    events.Select(e => new DiscordSelectComponentOption(
                        $"{e.Title} - {e.Date:yyyy-MM-dd}",
                        e.EventId.ToString()
                    )).ToList()
                );

                // Send the message with the dropdown
                var builder = new DiscordMessageBuilder()
                    .WithContent("Select an event:")
                    .AddComponents(selectMenu);

                await ctx.RespondAsync(builder);
                var responseMessage = await ctx.GetResponseAsync();

                if (responseMessage == null)
                {
                    _logger.LogWarning($"Failed to get response message for event selection");
                    return null;
                }

                // Wait for user to select an option
                var result = await responseMessage.WaitForSelectAsync(
                    ctx.User,
                    "event_select",
                    TimeSpan.FromMinutes(2)
                );

                if (result.TimedOut)
                {
                    _logger.LogInformation($"User {ctx.User.Id} timed out selecting an event");
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                        .WithContent("You took too long to respond. Please try again."));
                    return null;
                }

                if (!long.TryParse(result.Result.Values.First(), out long selectedEventId))
                {
                    _logger.LogWarning($"User {ctx.User.Id} selected invalid event ID");
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                        .WithContent("Invalid event selected. Please try again."));
                    return null;
                }

                var selectedEvent = events.First(e => e.EventId == selectedEventId);
                _logger.LogInformation($"User {ctx.User.Id} selected event {selectedEventId}: {selectedEvent.Title}");

                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"You selected: **{selectedEvent.Title}**."));

                return selectedEventId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error selecting event from database");

                // Try to respond with error if possible
                try
                {
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                        .WithContent("An error occurred while selecting an event. Please try again."));
                }
                catch { /* Ignore if this fails too */ }

                return null;
            }
        }

        public async Task<bool> RemoveAttendance(long eventId, long userId)
        {
            try
            {
                _logger.LogInformation($"Removing attendance for user {userId} from event {eventId} from database only");

                // Only remove from database now
                bool result = await _bot.RemoveAttendanceFromDb(eventId, userId);

                if (result)
                {
                    _logger.LogInformation($"Successfully removed attendance for user {userId} from event {eventId}");
                }
                else
                {
                    _logger.LogWarning($"Failed to remove attendance for user {userId} from event {eventId}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing attendance for user {userId} from event {eventId}");
                return false;
            }
        }

        public async Task<bool> AddAttendance(long eventId, long userId)
        {
            try
            {
                _logger.LogInformation($"Adding attendance for user {userId} to event {eventId} in database only");

                // Only add to database
                bool result = await _bot.AddAttendanceInDb(eventId, userId);

                if (result)
                {
                    _logger.LogInformation($"Successfully added attendance for user {userId} to event {eventId}");
                }
                else
                {
                    _logger.LogWarning($"Failed to add attendance for user {userId} to event {eventId}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding attendance for user {userId} to event {eventId}");
                return false;
            }
        }


        public async Task UpdateEvents()
        {
            try
            {
                _logger.LogInformation("Starting event update from RaidHelper");
                await _bot.UpdateEvents();
                _logger.LogInformation("Event update completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating events from RaidHelper");
                throw;
            }
        }
    }
}
