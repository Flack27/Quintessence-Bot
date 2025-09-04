using Google.Apis.Sheets.v4.Data;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Logging;
using QutieDAL.DAL;

public abstract class GoogleSheetsServiceBase
{
    protected readonly SheetsService _service;
    protected readonly ILogger<GoogleSheetsServiceBase> _logger;
    private readonly GoogleSheetsDAL _dal;

    // Add these rate limiting fields
    private static readonly SemaphoreSlim _sheetsApiSemaphore = new SemaphoreSlim(1, 1);
    private static DateTime _lastApiCall = DateTime.MinValue;
    private static readonly TimeSpan _minDelayBetweenCalls = TimeSpan.FromMilliseconds(100);

    protected GoogleSheetsServiceBase(SheetsService service, ILogger<GoogleSheetsServiceBase> logger, GoogleSheetsDAL dal)
    {
        _service = service;
        _logger = logger;
        _dal = dal;
    }

    // Replace your existing methods with rate-limited versions
    protected async Task<ValueRange> GetRangeValuesAsync(string spreadsheetId, string range)
    {
        return await ExecuteWithRateLimitAsync(async () =>
        {
            try
            {
                var request = _service.Spreadsheets.Values.Get(spreadsheetId, range);
                return await request.ExecuteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting range values for {spreadsheetId}, range {range}");
                throw;
            }
        });
    }

    protected async Task UpdateRangeValuesAsync(string spreadsheetId, string range, ValueRange valueRange)
    {
        await ExecuteWithRateLimitAsync(async () =>
        {
            try
            {
                var updateRequest = _service.Spreadsheets.Values.Update(valueRange, spreadsheetId, range);
                updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
                await updateRequest.ExecuteAsync();
                return true; // Return something for the generic method
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating range values for {spreadsheetId}, range {range}");
                throw;
            }
        });
    }

    // Add the rate limiting method
    private async Task<T> ExecuteWithRateLimitAsync<T>(Func<Task<T>> operation)
    {
        await _sheetsApiSemaphore.WaitAsync();
        try
        {
            var timeSinceLastCall = DateTime.UtcNow - _lastApiCall;
            if (timeSinceLastCall < _minDelayBetweenCalls)
            {
                var delayTime = _minDelayBetweenCalls - timeSinceLastCall;
                _logger.LogDebug($"Rate limiting: Waiting {delayTime.TotalMilliseconds}ms before next API call");
                await Task.Delay(delayTime);
            }

            var result = await operation();
            _lastApiCall = DateTime.UtcNow;
            return result;
        }
        finally
        {
            _sheetsApiSemaphore.Release();
        }
    }

    // Also add this for batch operations
    protected async Task ExecuteBatchUpdateAsync(string spreadsheetId, BatchUpdateSpreadsheetRequest batchRequest)
    {
        await ExecuteWithRateLimitAsync(async () =>
        {
            try
            {
                await _service.Spreadsheets.BatchUpdate(batchRequest, spreadsheetId).ExecuteAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error executing batch update for {spreadsheetId}");
                throw;
            }
        });
    }


    protected async Task<int> CreateTabIfNotExistsAsync(string spreadsheetId, string tabName, long channelId)
    {
        try
        {
            var spreadsheet = await ExecuteWithRateLimitAsync(async () =>
                await _service.Spreadsheets.Get(spreadsheetId).ExecuteAsync()
            );

            // Check if tab already exists
            foreach (var sheet in spreadsheet.Sheets)
            {
                if (sheet.Properties.Title == tabName)
                {
                    _logger.LogDebug($"Tab '{tabName}' already exists with ID: {sheet.Properties.SheetId}");
                    return (int)sheet.Properties.SheetId.Value;
                }
            }

            // Create new tab (let Google auto-generate ID)
            var request = new Request
            {
                AddSheet = new AddSheetRequest
                {
                    Properties = new SheetProperties
                    {
                        Title = tabName,
                        GridProperties = new GridProperties
                        {
                            RowCount = 1000,
                            ColumnCount = 26
                        }
                    }
                }
            };

            var batchRequest = new BatchUpdateSpreadsheetRequest
            {
                Requests = new List<Request> { request }
            };

            var response = await ExecuteWithRateLimitAsync(async () =>
                await _service.Spreadsheets.BatchUpdate(batchRequest, spreadsheetId).ExecuteAsync()
            );

            var newSheetId = (int)response.Replies[0].AddSheet.Properties.SheetId.Value;

            await _dal.SaveTabId(channelId, newSheetId);

            _logger.LogInformation($"Created tab '{tabName}' with ID: {newSheetId}");
            return newSheetId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error creating tab {tabName} in spreadsheet {spreadsheetId}");
            throw;
        }
    }
}