using DSharpPlus.EventArgs;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Logging;
using QutieBot.Bot.GoogleSheets;
using QutieDAL.DAL;
using QutieDTO.Models;
using System.Linq;

public class GoogleSheetsFacade
{
    private readonly UserSheetService _userService;
    private readonly EventSheetService _eventService;
    private readonly AttendanceSheetService _attendanceService;
    private readonly GoogleSheetsDAL _dal;
    private readonly ILogger<GoogleSheetsFacade> _logger;

    public GoogleSheetsFacade(
        UserSheetService userService,
        EventSheetService eventService,
        AttendanceSheetService attendanceService,
        GoogleSheetsDAL dal,
        ILogger<GoogleSheetsFacade> logger)
    {
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _attendanceService = attendanceService ?? throw new ArgumentNullException(nameof(attendanceService));
        _dal = dal ?? throw new ArgumentNullException(nameof(dal));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SyncUserDataAsync(bool sendNotifications = false)
    {
        _logger.LogInformation("Starting full data synchronization");

        try
        {
            var games = await _dal.GetGameData();
            if (games == null || !games.Any())
            {
                _logger.LogWarning("No games found to sync");
                return;
            }

            // Sync game users first
            foreach (var game in games)
            {
                await _userService.SyncUsersAsync(game, sendNotifications);

                // Then sync channel users
                foreach (var channel in game.Channels)
                {
                    await _userService.SyncChannelUsersAsync(channel);
                }
            }

            _logger.LogInformation("Full data synchronization completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during full data synchronization");
        }
    }

    public async Task ProcessBulkEventSignupsAsync(List<long> userIds, Event evt, bool isSignedUp)
    {
        if (evt == null || evt.Channel == null)
        {
            _logger.LogWarning($"Cannot process bulk event signups with null event or channel");
            return;
        }

        _logger.LogInformation($"Processing bulk event signup for {userIds.Count} users in event {evt.EventId}, signed up: {isSignedUp}");

        try
        {
            // Manually adjust the event's signup list to reflect the changes
            if (isSignedUp)
            {
                // Adding users - ensure they're in the signup list
                foreach (var userId in userIds)
                {
                    if (!evt.EventSignups.Any(s => s.UserId == userId))
                    {
                        evt.EventSignups.Add(new EventSignup { UserId = userId, EventId = evt.EventId });
                    }
                }
            }
            else
            {
                // Removing users - filter out the ones we're removing
                evt.EventSignups = evt.EventSignups
                    .Where(s => !userIds.Contains(s.UserId))
                    .ToList();
            }

            // Use the bulk prepare method to build updates for ALL users
            var requests = await _attendanceService.PrepareAttendanceRequestsAsync(evt, evt.Channel);

            if (requests.Count > 0)
            {
                var batchUpdateRequest = new BatchUpdateSpreadsheetRequest { Requests = requests };
                await _attendanceService.ExecuteBulkAttendanceUpdateAsync(evt.Channel.Game.SheetId, batchUpdateRequest);

                _logger.LogInformation($"Successfully processed bulk signup for {userIds.Count} users in event {evt.EventId}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing bulk event signup in event {evt.EventId}");
        }
    }

    public async Task ProcessRoleChangeAsync(GuildMemberUpdatedEventArgs e)
    {
        _logger.LogInformation($"Processing role change for user {e.Member.Id}");

        try
        {
            var games = await _dal.GetGameData();
            if (games == null || !games.Any())
            {
                _logger.LogWarning("No games found to process role change");
                return;
            }

            foreach (var game in games)
            {
                // Handle game role changes
                var gameRoleId = game.RoleId;
                bool hadGameRole = e.RolesBefore.Any(role => (long)role.Id == gameRoleId);
                bool hasGameRole = e.RolesAfter.Any(role => (long)role.Id == gameRoleId);

                if (hasGameRole && !hadGameRole)
                {
                    _logger.LogInformation($"User {e.Member.Id} gained role for game: {game.GameName}");
                    await _userService.AddOrUpdateUserAsync((long)e.Member.Id, game);
                }
                else if (hadGameRole && !hasGameRole)
                {
                    _logger.LogInformation($"User {e.Member.Id} lost role for game: {game.GameName}");
                    await _userService.DeleteUserAsync((long)e.Member.Id, game);
                }

                // Handle channel role changes
                foreach (var channel in game.Channels)
                {
                    bool hadChannelRole = e.RolesBefore.Any(role => (long)role.Id == channel.RoleId);
                    bool hasChannelRole = e.RolesAfter.Any(role => (long)role.Id == channel.RoleId);

                    if (hasChannelRole && !hadChannelRole)
                    {
                        _logger.LogInformation($"User {e.Member.Id} gained role for channel: {channel.ChannelName}");
                        await _userService.AddOrUpdateChannelUserAsync((long)e.Member.Id, game, channel);
                    }
                    else if (hadChannelRole && !hasChannelRole)
                    {
                        _logger.LogInformation($"User {e.Member.Id} lost role for channel: {channel.ChannelName}");
                        await _userService.DeleteChannelUserAsync((long)e.Member.Id, game, channel);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing role change for user {e.Member.Id}");
        }
    }


    public async Task UpdateUserAsync(long userId, Game game)
    {
        try
        {
            await _userService.AddOrUpdateUserAsync(userId, game);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing updating sheet for user {userId}");
        }
    }
    

    public async Task DeleteUserAsync(long userId, Game game)
    {
        try
        {
            await _userService.DeleteUserAsync(userId, game);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing sheet removal for user {userId}");
        }
    }


    public async Task ProcessEventSignupAsync(long userId, Event? evt, bool isSignedUp)
    {
        if (evt == null || evt.Channel == null)
        {
            _logger.LogWarning($"Cannot process event signup for user {userId} with null event or channel");
            return;
        }

        _logger.LogInformation($"Processing event signup for user {userId} in event {evt.EventId}, signed up: {isSignedUp}");

        try
        {
            // Update attendance in sheet
            await _attendanceService.UpdateAttendanceAsync(userId, evt, isSignedUp);

            _logger.LogInformation($"Successfully processed event signup for user {userId} in event {evt.EventId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing event signup for user {userId} in event {evt.EventId}");
        }
    }

    public async Task SyncEventsAsync(List<Event> events, Channel channel)
    {
        _logger.LogInformation("Starting event synchronization");

        try
        {
            if (events == null || !events.Any())
            {
                _logger.LogInformation($"No events found for channel: {channel.ChannelName}");
                return;
            }

            await _eventService.PopulateEventsAndSignupsAsync(events, channel, _attendanceService);

            _logger.LogInformation("Event synchronization completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during event synchronization");
        }
    }
}