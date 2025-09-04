using Google;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Logging;
using QutieDAL.DAL;
using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieBot.Bot.GoogleSheets
{
    // EventSheetService.cs
    public class EventSheetService : GoogleSheetsServiceBase
    {
        private readonly string _eventStartColumn = "H";
        private readonly int _eventStartRow = 7;

        public EventSheetService(SheetsService service, ILogger<EventSheetService> logger, GoogleSheetsDAL dal)
            : base(service, logger, dal)
        {
            // Constructor
        }

        public async Task PopulateEventsAndSignupsAsync(List<Event> events, Channel channel, AttendanceSheetService attendanceService)
        {
            if (events == null || !events.Any())
            {
                _logger.LogWarning($"No events to populate for channel: {channel.ChannelName}");
                return;
            }

            if (channel == null || channel.Game == null)
            {
                _logger.LogWarning("Cannot populate events for null channel or game");
                return;
            }

            // Ensure tab exists
            int tabId = await CreateTabIfNotExistsAsync(channel.Game.SheetId, channel.ChannelName, channel.ChannelId);
            channel.SheetTabId = tabId;

            _logger.LogInformation($"Populating {events.Count} events for channel: {channel.ChannelName}");

            foreach (var evt in events)
            {
                try
                {
                    await AddOrUpdateEventAsync(evt, channel);

                    var requests = await attendanceService.PrepareAttendanceRequestsAsync(evt, channel);
                    if (requests.Count > 0)
                    {
                        var batchUpdateRequest = new BatchUpdateSpreadsheetRequest { Requests = requests };
                        await ExecuteBatchUpdateAsync(channel.Game.SheetId, batchUpdateRequest);
                    }

                    _logger.LogInformation($"Successfully populated event {evt.EventId}: {evt.Title}");
                }
                catch (GoogleApiException ex)
                {
                    _logger.LogError(ex, $"Google API error processing event {evt.EventId}: {evt.Title}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Unexpected error processing event {evt.EventId}: {evt.Title}");
                }
            }
        }

        public async Task AddOrUpdateEventAsync(Event evt, Channel channel)
        {
            if (evt == null)
            {
                _logger.LogWarning("Cannot add/update null event");
                return;
            }

            if (channel == null || channel.Game == null)
            {
                _logger.LogWarning($"Cannot add/update event {evt.EventId} for null channel or game");
                return;
            }

            _logger.LogInformation($"Adding/updating event {evt.EventId}: {evt.Title}");

            try
            {
                // Get existing events data
                string dataRange = $"{channel.ChannelName}!{_eventStartColumn}{_eventStartRow}:{_eventStartRow}";
                var response = await GetRangeValuesAsync(channel.Game.SheetId, dataRange);
                var existingValues = response.Values;

                // Find if event already exists
                int? targetColumnIndex = null;
                if (existingValues != null)
                {
                    for (int col = 0; col < existingValues[0]?.Count; col++)
                    {
                        if (existingValues[0][col]?.ToString() == evt.EventId.ToString())
                        {
                            targetColumnIndex = col;
                            break;
                        }
                    }
                }

                // Calculate column position
                var columnStartIndex = targetColumnIndex ?? (existingValues?[0]?.Count ?? 0);
                var columnLetter = SheetUtils.GetColumnLetter(_eventStartColumn[0] - 'A' + columnStartIndex);

                // Prepare event data
                var rowData = new List<IList<object>>
            {
                new List<object> { evt.EventId.ToString() },
                new List<object> { evt.Title },
                new List<object> { evt.Date.ToString("yyyy-MM-dd") }
            };

                // Define range and update sheet
                string targetRange = $"{channel.ChannelName}!{columnLetter}{_eventStartRow}:{columnLetter}{_eventStartRow + 2}";
                var valueRange = new ValueRange { Values = rowData };

                await UpdateRangeValuesAsync(channel.Game.SheetId, targetRange, valueRange);
                _logger.LogInformation($"Successfully updated event data for {evt.EventId}: {evt.Title}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating event {evt.EventId}: {evt.Title}");
                throw;
            }
        }

        public async Task<int?> FindEventColumnIndexAsync(Event evt, Channel channel)
        {
            if (evt == null || channel == null || channel.Game == null)
            {
                return null;
            }

            try
            {
                string eventRange = $"{channel.ChannelName}!{_eventStartColumn}{_eventStartRow}:{_eventStartRow}";
                var eventResponse = await GetRangeValuesAsync(channel.Game.SheetId, eventRange);
                var eventValues = eventResponse.Values;

                if (eventValues != null)
                {
                    for (int col = 0; col < eventValues[0]?.Count; col++)
                    {
                        if (eventValues[0][col]?.ToString() == evt.EventId.ToString())
                        {
                            return col;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error finding column index for event {evt.EventId}");
                return null;
            }
        }
    }
}
