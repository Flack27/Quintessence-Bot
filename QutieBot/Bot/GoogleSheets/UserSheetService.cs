using DSharpPlus;
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
    // UserSheetService.cs
    public class UserSheetService : GoogleSheetsServiceBase
    {
        private readonly string _startColumn = "C";
        private readonly int _startRow = 10;
        private readonly UserSheetDAL _dal;
        private readonly DiscordClient _client;

        public UserSheetService(
            SheetsService service,
            GoogleSheetsDAL dal,
            UserSheetDAL userSheetDAL,
            DiscordClient client,
            ILogger<UserSheetService> logger)
            : base(service, logger, dal)
        {
            _dal = userSheetDAL ?? throw new ArgumentNullException(nameof(dal));
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task SyncUsersAsync(Game game, bool sendNotifications = false)
        {
            if (game == null)
            {
                _logger.LogWarning("Attempted to sync users for null game");
                return;
            }

            _logger.LogInformation($"Starting user sync for game: {game.GameName}");

            try
            {
                // Get users with the game role
                var userIds = await _dal.GetUserIdsWithGameRoleAsync(game.GameId);
                if (userIds == null || !userIds.Any())
                {
                    _logger.LogInformation($"No users found with role for game: {game.GameName}");
                    return;
                }

                // Get existing user data from the sheet
                string dataRange = $"{_startColumn}{_startRow}:{_startColumn}";
                var response = await GetRangeValuesAsync(game.SheetId, dataRange);
                var existingValues = response.Values;

                // Extract existing user IDs and their row positions
                var existingUserIds = new Dictionary<long, int>();
                if (existingValues != null)
                {
                    for (int i = 0; i < existingValues.Count; i++)
                    {
                        if (existingValues[i].Count > 0 && long.TryParse(existingValues[i][0].ToString(), out var existingId))
                        {
                            existingUserIds[existingId] = _startRow + i;
                        }
                    }
                }

                // Find users to delete (in sheet but not in role)
                var usersToDelete = existingUserIds.Keys.Except(userIds).ToList();

                // Track users with missing data for notifications
                var missingDataUsers = new HashSet<ulong>();

                // Prepare batch update requests
                var batchUpdateRequest = new BatchUpdateSpreadsheetRequest
                {
                    Requests = new List<Request>()
                };

                // Process existing users
                foreach (var userId in userIds.Where(id => existingUserIds.ContainsKey(id)))
                {
                    var userData = await _dal.GetUserGameData(userId, game.GameId);
                    if (userData == null)
                    {
                        missingDataUsers.Add((ulong)userId);
                        continue;
                    }

                    // Check if user has meaningful data
                    var meaningfulFieldCount = userData.Count(value => !string.IsNullOrWhiteSpace(value));
                    if (meaningfulFieldCount <= 3)
                    {
                        missingDataUsers.Add((ulong)userId);
                    }

                    // Add update request
                    int rowIndex = existingUserIds[userId];
                    var updateRequest = CreateUpdateCellsRequest(
                        game.SheetId,
                        rowIndex - 1, // Convert to 0-based
                        rowIndex,
                        _startColumn[0] - 'A',
                        PrepareUserRowData(userId, userData));

                    batchUpdateRequest.Requests.Add(updateRequest);
                }

                // Prepare new users to add
                var rowsToAdd = new List<IList<object>>();
                foreach (var userId in userIds.Where(id => !existingUserIds.ContainsKey(id)))
                {
                    var userData = await _dal.GetUserGameData(userId, game.GameId);
                    if (userData == null)
                    {
                        missingDataUsers.Add((ulong)userId);
                        continue;
                    }

                    // Check if user has meaningful data
                    var meaningfulFieldCount = userData.Count(value => !string.IsNullOrWhiteSpace(value));
                    if (meaningfulFieldCount <= 3)
                    {
                        missingDataUsers.Add((ulong)userId);
                    }

                    var newRow = new List<object> { userId.ToString() };
                    newRow.AddRange(userData);
                    rowsToAdd.Add(newRow);
                }

                // 1. Delete users first (prevents row index conflicts)
                if (usersToDelete.Any())
                {
                    _logger.LogInformation($"Deleting {usersToDelete.Count} users no longer in role for game: {game.GameName}");
                    await DeleteUsersFromSheet(game.SheetId, usersToDelete, existingUserIds);
                }

                // 2. Re-fetch data after deletions to get correct row indices
                var refreshedResponse = await GetRangeValuesAsync(game.SheetId, dataRange);
                var refreshedValues = refreshedResponse.Values;
                var refreshedUserIds = new Dictionary<long, int>();
                if (refreshedValues != null)
                {
                    for (int i = 0; i < refreshedValues.Count; i++)
                    {
                        if (refreshedValues[i].Count > 0 && long.TryParse(refreshedValues[i][0].ToString(), out var existingId))
                        {
                            refreshedUserIds[existingId] = _startRow + i;
                        }
                    }
                }

                // 3. Rebuild update requests with correct row indices
                var refreshedBatchRequest = new BatchUpdateSpreadsheetRequest
                {
                    Requests = new List<Request>()
                };

                foreach (var userId in userIds.Where(id => refreshedUserIds.ContainsKey(id)))
                {
                    var userData = await _dal.GetUserGameData(userId, game.GameId);
                    if (userData == null) continue;

                    int rowIndex = refreshedUserIds[userId];
                    var updateRequest = CreateUpdateCellsRequest(
                        game.SheetId,
                        rowIndex - 1,
                        rowIndex,
                        _startColumn[0] - 'A',
                        PrepareUserRowData(userId, userData));

                    refreshedBatchRequest.Requests.Add(updateRequest);
                }

                // 4. Execute updates
                if (refreshedBatchRequest.Requests.Any())
                {
                    _logger.LogInformation($"Updating {refreshedBatchRequest.Requests.Count} existing users for game: {game.GameName}");
                    await _service.Spreadsheets.BatchUpdate(refreshedBatchRequest, game.SheetId).ExecuteAsync();
                }

                // 5. Add new users at the end
                if (rowsToAdd.Any())
                {
                    _logger.LogInformation($"Adding {rowsToAdd.Count} new users for game: {game.GameName}");
                    int currentUserCount = refreshedValues?.Count ?? 0;
                    string addRange = $"{_startColumn}{_startRow + currentUserCount}:{_startColumn}";
                    var addValueRange = new ValueRange { Values = rowsToAdd };

                    var appendRequest = _service.Spreadsheets.Values.Append(addValueRange, game.SheetId, addRange);
                    appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
                    await appendRequest.ExecuteAsync();
                }

                // Notify users with missing data
                if (missingDataUsers.Any() && sendNotifications)
                {
                    await NotifyMissingDataUsersAsync(missingDataUsers, game);
                }

                _logger.LogInformation($"User sync completed for game: {game.GameName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during user sync for game: {game.GameName}");
            }
        }

        private Request CreateUpdateCellsRequest(string sheetId, int startRowIndex, int endRowIndex, int startColumnIndex, List<CellData> cellData)
        {
            return new Request
            {
                UpdateCells = new UpdateCellsRequest
                {
                    Range = new GridRange
                    {
                        SheetId = 0,
                        StartRowIndex = startRowIndex,
                        EndRowIndex = endRowIndex,
                        StartColumnIndex = startColumnIndex,
                        EndColumnIndex = startColumnIndex + cellData.Count
                    },
                    Rows = new List<RowData>
                {
                    new RowData { Values = cellData }
                },
                    Fields = "userEnteredValue"
                }
            };
        }

        private List<CellData> PrepareUserRowData(long userId, List<string> userData)
        {
            var cellData = new List<CellData>
        {
            new CellData { UserEnteredValue = new ExtendedValue { StringValue = userId.ToString() } }
        };

            foreach (var value in userData)
            {
                cellData.Add(new CellData { UserEnteredValue = new ExtendedValue { StringValue = value } });
            }

            return cellData;
        }

        private async Task DeleteUsersFromSheet(string sheetId, List<long> usersToDelete, Dictionary<long, int> existingUserIds)
        {
            var batchRequest = new BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Request>()
            };

            var rowsToDelete = usersToDelete
                .Select(userId => existingUserIds.TryGetValue(userId, out var rowIndex) ? rowIndex : -1)
                .Where(rowIndex => rowIndex >= 0)
                .OrderByDescending(rowIndex => rowIndex)
                .ToList();

            foreach (var rowIndex in rowsToDelete)
            {
                batchRequest.Requests.Add(new Request
                {
                    DeleteDimension = new DeleteDimensionRequest
                    {
                        Range = new DimensionRange
                        {
                            Dimension = "ROWS",
                            StartIndex = rowIndex - 1, 
                            EndIndex = rowIndex
                        }
                    }
                });
            }

            if (batchRequest.Requests.Any())
            {
                await _service.Spreadsheets.BatchUpdate(batchRequest, sheetId).ExecuteAsync();
            }
        }

        private async Task NotifyMissingDataUsersAsync(HashSet<ulong> missingDataUsers, Game game)
        {
            if (missingDataUsers.Count == 0)
            {
                return;
            }

            _logger.LogInformation($"Notifying {missingDataUsers.Count} users with missing data for game: {game.GameName}");

            try
            {
                var channel = await _client.GetChannelAsync((ulong)game.ChannelId);
                const int delayBetweenMessagesMs = 100;

                foreach (var userId in missingDataUsers)
                {
                    try
                    {
                        var user = await _client.GetUserAsync(userId);
                        await channel.SendMessageAsync($"{user.Mention}, it looks like your game data is missing! Please fill it out as soon as possible.");
                        await Task.Delay(delayBetweenMessagesMs);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to notify user {userId} for game: {game.GameName}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to access notification channel for game: {game.GameName}");
            }
        }

        public async Task AddOrUpdateUserAsync(long userId, Game game)
        {
            if (game == null)
            {
                _logger.LogWarning($"Attempted to add/update user {userId} for null game");
                return;
            }

            _logger.LogInformation($"Adding/updating user {userId} for game: {game.GameName}");

            try
            {
                var userData = await _dal.GetUserGameData(userId, game.GameId);
                if (userData == null)
                {
                    _logger.LogWarning($"No data found for user {userId} in game: {game.GameName}");
                    return;
                }

                // Find if user already exists in sheet
                string dataRange = $"{_startColumn}{_startRow}:{_startColumn}";
                var response = await GetRangeValuesAsync(game.SheetId, dataRange);
                var values = response.Values;

                // Find existing user row if any
                int? targetRow = null;
                if (values != null)
                {
                    for (int i = 0; i < values.Count; i++)
                    {
                        if (values[i].Count > 0 && values[i][0].ToString() == userId.ToString())
                        {
                            targetRow = _startRow + i;
                            break;
                        }
                    }
                }

                // Prepare data
                var rowData = new List<object> { userId.ToString() };
                rowData.AddRange(userData);

                // Calculate range
                int startColumnIndex = _startColumn[0] - 'A';
                string startingLetter = SheetUtils.GetColumnLetter(startColumnIndex);
                string endingLetter = SheetUtils.GetColumnLetter(startColumnIndex + rowData.Count - 1);

                string targetRange;
                if (targetRow.HasValue)
                {
                    targetRange = $"{startingLetter}{targetRow.Value}:{endingLetter}{targetRow.Value}";
                }
                else
                {
                    targetRow = (values?.Count ?? 0) + _startRow;
                    targetRange = $"{startingLetter}{targetRow}:{endingLetter}{targetRow}";
                }

                // Update or insert
                var valueRange = new ValueRange { Values = new List<IList<object>> { rowData } };
                await UpdateRangeValuesAsync(game.SheetId, targetRange, valueRange);

                _logger.LogInformation($"Successfully updated user {userId} for game: {game.GameName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding/updating user {userId} for game: {game.GameName}");
            }
        }

        public async Task DeleteUserAsync(long userId, Game game)
        {
            if (game == null)
            {
                _logger.LogWarning($"Attempted to delete user {userId} from null game");
                return;
            }

            _logger.LogInformation($"Deleting user {userId} from game: {game.GameName}");

            try
            {
                // Get existing user data
                string dataRange = $"{_startColumn}{_startRow}:{_startColumn}";
                var response = await GetRangeValuesAsync(game.SheetId, dataRange);
                var values = response.Values;

                // Find the user's row
                int? rowToDelete = null;
                if (values != null)
                {
                    for (int i = 0; i < values.Count; i++)
                    {
                        if (values[i].Count > 0 && values[i][0].ToString() == userId.ToString())
                        {
                            rowToDelete = _startRow + i;
                            break;
                        }
                    }
                }

                if (rowToDelete != null)
                {
                    var deleteRequest = new BatchUpdateSpreadsheetRequest
                    {
                        Requests = new List<Request>
                    {
                        new Request
                        {
                            DeleteDimension = new DeleteDimensionRequest
                            {
                                Range = new DimensionRange
                                {
                                    Dimension = "ROWS",
                                    StartIndex = rowToDelete.Value - 1, 
                                    EndIndex = rowToDelete.Value
                                }
                            }
                        }
                    }
                    };

                    await _service.Spreadsheets.BatchUpdate(deleteRequest, game.SheetId).ExecuteAsync();
                    _logger.LogInformation($"Successfully deleted user {userId} from game: {game.GameName}");
                }
                else
                {
                    _logger.LogWarning($"User {userId} not found in sheet for game: {game.GameName}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting user {userId} from game: {game.GameName}");
            }
        }

        public async Task AddOrUpdateChannelUserAsync(long userId, Game game, Channel channel)
        {
            if (game == null || channel == null)
            {
                _logger.LogWarning($"Attempted to add/update user {userId} for null game or channel");
                return;
            }

            _logger.LogInformation($"Adding/updating user {userId} for channel: {channel.ChannelName}");

            try
            {
                // Ensure tab exists
                int tabId = await CreateTabIfNotExistsAsync(game.SheetId, channel.ChannelName, channel.ChannelId);

                // Get user data for this channel
                var userData = await _dal.GetUserChannelData(userId, channel.ChannelId);
                if (userData == null)
                {
                    _logger.LogWarning($"No data found for user {userId} in channel: {channel.ChannelName}");
                    return;
                }

                // Find if user already exists in channel tab
                string dataRange = $"{channel.ChannelName}!{_startColumn}{_startRow}:{_startColumn}";
                var response = await GetRangeValuesAsync(game.SheetId, dataRange);
                var values = response.Values;

                // Find existing user row if any
                int? targetRow = null;
                if (values != null)
                {
                    for (int i = 0; i < values.Count; i++)
                    {
                        if (values[i].Count > 0 && values[i][0].ToString() == userId.ToString())
                        {
                            targetRow = _startRow + i;
                            break;
                        }
                    }
                }

                // Prepare data
                var rowData = new List<object> { userId.ToString() };
                rowData.AddRange(userData);

                // Calculate range
                int startColumnIndex = _startColumn[0] - 'A';
                string startingLetter = SheetUtils.GetColumnLetter(startColumnIndex);
                string endingLetter = SheetUtils.GetColumnLetter(startColumnIndex + rowData.Count - 1);

                string targetRange;
                if (targetRow.HasValue)
                {
                    targetRange = $"{channel.ChannelName}!{startingLetter}{targetRow.Value}:{endingLetter}{targetRow.Value}";
                }
                else
                {
                    targetRow = (values?.Count ?? 0) + _startRow;
                    targetRange = $"{channel.ChannelName}!{startingLetter}{targetRow}:{endingLetter}{targetRow}";
                }

                // Update or insert
                var valueRange = new ValueRange { Values = new List<IList<object>> { rowData } };
                await UpdateRangeValuesAsync(game.SheetId, targetRange, valueRange);

                _logger.LogInformation($"Successfully updated user {userId} for channel: {channel.ChannelName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding/updating user {userId} for channel: {channel.ChannelName}");
            }
        }

        public async Task DeleteChannelUserAsync(long userId, Game game, Channel channel)
        {
            if (game == null || channel == null)
            {
                _logger.LogWarning($"Attempted to delete user {userId} from null game or channel");
                return;
            }

            _logger.LogInformation($"Deleting user {userId} from channel: {channel.ChannelName}");

            try
            {
                // Get existing user data
                string dataRange = $"{channel.ChannelName}!{_startColumn}{_startRow}:{_startColumn}";
                var response = await GetRangeValuesAsync(game.SheetId, dataRange);
                var values = response.Values;

                // Find the user's row
                int? rowToDelete = null;
                if (values != null)
                {
                    for (int i = 0; i < values.Count; i++)
                    {
                        if (values[i].Count > 0 && values[i][0].ToString() == userId.ToString())
                        {
                            rowToDelete = _startRow + i;
                            break;
                        }
                    }
                }

                if (rowToDelete != null)
                {
                    var deleteRequest = new BatchUpdateSpreadsheetRequest
                    {
                        Requests = new List<Request>
                        {
                            new Request
                            {
                                DeleteDimension = new DeleteDimensionRequest
                                {
                                    Range = new DimensionRange
                                    {
                                        SheetId = channel.SheetTabId,
                                        Dimension = "ROWS",
                                        StartIndex = rowToDelete.Value - 1,
                                        EndIndex = rowToDelete.Value
                                    }
                                }
                            }
                        }
                    };

                    await _service.Spreadsheets.BatchUpdate(deleteRequest, game.SheetId).ExecuteAsync();
                    _logger.LogInformation($"Successfully deleted user {userId} from channel: {channel.ChannelName}");
                }
                else
                {
                    _logger.LogWarning($"User {userId} not found in channel tab: {channel.ChannelName}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting user {userId} from channel: {channel.ChannelName}");
            }
        }

        public async Task SyncChannelUsersAsync(Channel channel)
        {
            if (channel == null || channel.Game == null || channel.RoleId == null)
            {
                _logger.LogWarning("Attempted to sync users for null channel");
                return;
            }

            _logger.LogInformation($"Starting user sync for channel: {channel.ChannelName}");

            try
            {
                // Ensure tab exists
                int tabId = await CreateTabIfNotExistsAsync(channel.Game.SheetId, channel.ChannelName, channel.ChannelId);

                // Get users with the channel role
                var userIds = await _dal.GetUserIdsWithRoleAsync(channel.RoleId.Value);
                if (userIds == null || !userIds.Any())
                {
                    _logger.LogInformation($"No users found with role for channel: {channel.ChannelName}");
                    return;
                }

                // Get existing user data from the sheet
                string dataRange = $"{channel.ChannelName}!{_startColumn}{_startRow}:{_startColumn}";
                var response = await GetRangeValuesAsync(channel.Game.SheetId, dataRange);
                var existingValues = response.Values;

                // Extract existing user IDs and their row positions
                var existingUserIds = new Dictionary<long, int>();
                if (existingValues != null)
                {
                    for (int i = 0; i < existingValues.Count; i++)
                    {
                        if (existingValues[i].Count > 0 && long.TryParse(existingValues[i][0].ToString(), out var existingId))
                        {
                            existingUserIds[existingId] = _startRow + i;
                        }
                    }
                }

                // Find users to delete (in sheet but not in role)
                var usersToDelete = existingUserIds.Keys.Except(userIds).ToList();

                // Prepare batch update requests
                var batchUpdateRequest = new BatchUpdateSpreadsheetRequest
                {
                    Requests = new List<Request>()
                };

                // Process existing users
                foreach (var userId in userIds.Where(id => existingUserIds.ContainsKey(id)))
                {
                    var userData = await _dal.GetUserChannelData(userId, channel.ChannelId);
                    if (userData == null)
                    {
                        continue;
                    }

                    // Add update request
                    int rowIndex = existingUserIds[userId];
                    var updateRequest = CreateUpdateCellsRequest(
                        tabId.ToString(),
                        rowIndex - 1, // Convert to 0-based
                        rowIndex,
                        _startColumn[0] - 'A',
                        PrepareUserRowData(userId, userData));

                    batchUpdateRequest.Requests.Add(updateRequest);
                }

                // Prepare new users to add
                var rowsToAdd = new List<IList<object>>();
                foreach (var userId in userIds.Where(id => !existingUserIds.ContainsKey(id)))
                {
                    var userData = await _dal.GetUserChannelData(userId, channel.ChannelId);
                    if (userData == null)
                    {
                        continue;
                    }

                    var newRow = new List<object> { userId.ToString() };
                    newRow.AddRange(userData);
                    rowsToAdd.Add(newRow);
                }

                // 1. Delete users first
                if (usersToDelete.Any())
                {
                    _logger.LogInformation($"Deleting users no longer in role for channel: {channel.ChannelName}");
                    await DeleteChannelUsersFromSheet(channel.Game.SheetId, tabId, usersToDelete, existingUserIds);
                }

                // 2. Re-fetch data after deletions
                string refreshDataRange = $"{channel.ChannelName}!{_startColumn}{_startRow}:{_startColumn}";
                var refreshedResponse = await GetRangeValuesAsync(channel.Game.SheetId, refreshDataRange);
                var refreshedValues = refreshedResponse.Values;
                var refreshedUserIds = new Dictionary<long, int>();
                if (refreshedValues != null)
                {
                    for (int i = 0; i < refreshedValues.Count; i++)
                    {
                        if (refreshedValues[i].Count > 0 && long.TryParse(refreshedValues[i][0].ToString(), out var existingId))
                        {
                            refreshedUserIds[existingId] = _startRow + i;
                        }
                    }
                }

                // 3. Rebuild update requests with correct row indices
                var refreshedBatchRequest = new BatchUpdateSpreadsheetRequest
                {
                    Requests = new List<Request>()
                };

                foreach (var userId in userIds.Where(id => refreshedUserIds.ContainsKey(id)))
                {
                    var userData = await _dal.GetUserChannelData(userId, channel.ChannelId);
                    if (userData == null) continue;

                    int rowIndex = refreshedUserIds[userId];
                    var updateRequest = CreateUpdateCellsRequest(
                        tabId.ToString(),
                        rowIndex - 1,
                        rowIndex,
                        _startColumn[0] - 'A',
                        PrepareUserRowData(userId, userData));

                    refreshedBatchRequest.Requests.Add(updateRequest);
                }

                // 4. Execute updates
                if (refreshedBatchRequest.Requests.Any())
                {
                    _logger.LogInformation($"Updating {refreshedBatchRequest.Requests.Count} existing users for channel: {channel.ChannelName}");
                    await _service.Spreadsheets.BatchUpdate(refreshedBatchRequest, channel.Game.SheetId).ExecuteAsync();
                }

                // 5. Add new users at the end
                if (rowsToAdd.Any())
                {
                    _logger.LogInformation($"Adding {rowsToAdd.Count} new users for channel: {channel.ChannelName}");
                    int currentUserCount = refreshedValues?.Count ?? 0;
                    string addRange = $"{channel.ChannelName}!{_startColumn}{_startRow + currentUserCount}:{_startColumn}";
                    var addValueRange = new ValueRange { Values = rowsToAdd };

                    var appendRequest = _service.Spreadsheets.Values.Append(addValueRange, channel.Game.SheetId, addRange);
                    appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
                    await appendRequest.ExecuteAsync();
                }

                _logger.LogInformation($"User sync completed for channel: {channel.ChannelName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during user sync for channel: {channel.ChannelName}");
            }
        }

        private async Task DeleteChannelUsersFromSheet(string sheetId, int tabId, List<long> usersToDelete, Dictionary<long, int> existingUserIds)
        {
            var batchRequest = new BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Request>()
            };

            var rowsToDelete = usersToDelete
                .Select(userId => existingUserIds.TryGetValue(userId, out var rowIndex) ? rowIndex : -1)
                .Where(rowIndex => rowIndex >= 0)
                .OrderByDescending(rowIndex => rowIndex)
                .ToList();

            foreach (var rowIndex in rowsToDelete)
            {
                batchRequest.Requests.Add(new Request
                {
                    DeleteDimension = new DeleteDimensionRequest
                    {
                        Range = new DimensionRange
                        {
                            SheetId = tabId,
                            Dimension = "ROWS",
                            StartIndex = rowIndex - 1, // Convert to 0-based
                            EndIndex = rowIndex
                        }
                    }
                });
            }

            if (batchRequest.Requests.Any())
            {
                await _service.Spreadsheets.BatchUpdate(batchRequest, sheetId).ExecuteAsync();
            }
        }
    }
}