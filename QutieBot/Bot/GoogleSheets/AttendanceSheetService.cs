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
    // AttendanceSheetService.cs
    public class AttendanceSheetService : GoogleSheetsServiceBase
    {
        private readonly string _startColumn = "C";
        private readonly int _startRow = 10;
        private readonly string _eventStartColumn = "H";
        private readonly int _eventStartRow = 7;

        public AttendanceSheetService(SheetsService service, ILogger<AttendanceSheetService> logger, GoogleSheetsDAL dal)
            : base(service, logger, dal)
        {
            // Constructor
        }

        public async Task UpdateAttendanceAsync(long userId, Event evt, bool isSignedUp)
        {
            if (evt == null || evt.Channel == null || evt.Channel.Game == null)
            {
                _logger.LogWarning($"Cannot update attendance for user {userId} with null event or missing channel/game");
                return;
            }

            var channel = evt.Channel;
            _logger.LogInformation($"Updating attendance for user {userId} in event {evt.EventId}, signed up: {isSignedUp}");

            // Ensure tab exists
            int tabId = await CreateTabIfNotExistsAsync(channel.Game.SheetId, channel.ChannelName, channel.ChannelId);
            try
            {
                // Find event column
                string eventRange = $"{channel.ChannelName}!{_eventStartColumn}{_eventStartRow}:{_eventStartRow}";
                var eventResponse = await GetRangeValuesAsync(channel.Game.SheetId, eventRange);
                var eventValues = eventResponse.Values;

                int? eventColumnIndex = null;
                if (eventValues != null)
                {
                    for (int col = 0; col < eventValues[0]?.Count; col++)
                    {
                        if (eventValues[0][col]?.ToString() == evt.EventId.ToString())
                        {
                            eventColumnIndex = col;
                            break;
                        }
                    }
                }

                if (eventColumnIndex == null)
                {
                    _logger.LogWarning($"Event {evt.EventId} not found in sheet for channel: {channel.ChannelName}");
                    return;
                }

                // Find user row
                string userRange = $"{channel.ChannelName}!{_startColumn}{_startRow}:{_startColumn}";
                var userResponse = await GetRangeValuesAsync(channel.Game.SheetId, userRange);
                var userValues = userResponse.Values;

                int? userRowIndex = null;
                if (userValues != null)
                {
                    for (int row = 0; row < userValues.Count; row++)
                    {
                        if (userValues[row]?.FirstOrDefault()?.ToString() == userId.ToString())
                        {
                            userRowIndex = row + _startRow; // Adjust to 1-based indexing
                            break;
                        }
                    }
                }

                if (userRowIndex == null)
                {
                    _logger.LogWarning($"User {userId} not found in sheet for channel: {channel.ChannelName}");
                    return;
                }

                // Update attendance cell with checkbox
                var requests = new List<Request>
            {
                new Request
                {
                    UpdateCells = new UpdateCellsRequest
                    {
                        Range = new GridRange
                        {
                            SheetId = tabId,
                            StartRowIndex = userRowIndex.Value - 1, // Convert to 0-based
                            EndRowIndex = userRowIndex.Value,
                            StartColumnIndex = _eventStartColumn[0] - 'A' + eventColumnIndex.Value,
                            EndColumnIndex = _eventStartColumn[0] - 'A' + eventColumnIndex.Value + 1
                        },
                        Rows = new List<RowData>
                        {
                            new RowData
                            {
                                Values = new List<CellData>
                                {
                                    new CellData
                                    {
                                        UserEnteredValue = new ExtendedValue { BoolValue = isSignedUp },
                                        DataValidation = new DataValidationRule
                                        {
                                            Condition = new BooleanCondition { Type = "BOOLEAN" },
                                            Strict = true
                                        }
                                    }
                                }
                            }
                        },
                        Fields = "userEnteredValue,dataValidation"
                    }
                }
            };

                var batchUpdateRequest = new BatchUpdateSpreadsheetRequest { Requests = requests };
                await _service.Spreadsheets.BatchUpdate(batchUpdateRequest, channel.Game.SheetId).ExecuteAsync();

                _logger.LogInformation($"Successfully updated attendance for user {userId} in event {evt.EventId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating attendance for user {userId} in event {evt.EventId}");
            }
        }

        public async Task<List<Request>> PrepareAttendanceRequestsAsync(Event evt, Channel channel)
        {
            var requests = new List<Request>();

            if (evt == null || channel == null || channel.Game == null)
            {
                _logger.LogWarning("Cannot prepare attendance requests for null event or channel");
                return requests;
            }

            _logger.LogInformation($"Preparing attendance requests for event {evt.EventId} in channel: {channel.ChannelName}");

            try
            {
                // Find event column
                string eventRange = $"{channel.ChannelName}!{_eventStartColumn}{_eventStartRow}:{_eventStartRow}";
                var eventResponse = await GetRangeValuesAsync(channel.Game.SheetId, eventRange);
                var eventValues = eventResponse.Values;

                int? eventColumnIndex = null;
                if (eventValues != null)
                {
                    for (int col = 0; col < eventValues[0]?.Count; col++)
                    {
                        if (eventValues[0][col]?.ToString() == evt.EventId.ToString())
                        {
                            eventColumnIndex = col;
                            break;
                        }
                    }
                }

                if (eventColumnIndex == null)
                {
                    _logger.LogWarning($"Event {evt.EventId} not found in sheet");
                    return requests;
                }

                // Get user IDs from sheet
                string userRange = $"{channel.ChannelName}!{_startColumn}{_startRow}:{_startColumn}";
                var userResponse = await GetRangeValuesAsync(channel.Game.SheetId, userRange);
                var userValues = userResponse.Values;

                if (userValues == null || userValues.Count == 0)
                {
                    _logger.LogWarning("No users found in sheet");
                    return requests;
                }

                // Prepare checkbox updates for each user
                int currentRow = _startRow;
                foreach (var userRow in userValues)
                {
                    string userId = userRow.FirstOrDefault()?.ToString();
                    if (string.IsNullOrEmpty(userId))
                    {
                        currentRow++;
                        continue;
                    }

                    bool isSignedUp = evt.EventSignups.Any(signup => signup.UserId.ToString() == userId);

                    requests.Add(new Request
                    {
                        UpdateCells = new UpdateCellsRequest
                        {
                            Range = new GridRange
                            {
                                SheetId = channel.SheetTabId,
                                StartRowIndex = currentRow - 1, // Convert to 0-based
                                EndRowIndex = currentRow,
                                StartColumnIndex = _eventStartColumn[0] - 'A' + eventColumnIndex.Value,
                                EndColumnIndex = _eventStartColumn[0] - 'A' + eventColumnIndex.Value + 1
                            },
                            Rows = new List<RowData>
                        {
                            new RowData
                            {
                                Values = new List<CellData>
                                {
                                    new CellData
                                    {
                                        UserEnteredValue = new ExtendedValue { BoolValue = isSignedUp },
                                        DataValidation = new DataValidationRule
                                        {
                                            Condition = new BooleanCondition { Type = "BOOLEAN" },
                                            Strict = true
                                        }
                                    }
                                }
                            }
                        },
                            Fields = "userEnteredValue,dataValidation"
                        }
                    });

                    currentRow++;
                }

                _logger.LogInformation($"Prepared {requests.Count} attendance updates for event {evt.EventId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error preparing attendance requests for event {evt.EventId}");
            }

            return requests;
        }
    }
}
