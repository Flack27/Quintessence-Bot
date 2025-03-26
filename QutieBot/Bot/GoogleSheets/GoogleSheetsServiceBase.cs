using Google.Apis.Sheets.v4.Data;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QutieDAL.DAL;

namespace QutieBot.Bot.GoogleSheets
{
    public abstract class GoogleSheetsServiceBase
    {
        protected readonly SheetsService _service;
        protected readonly ILogger<GoogleSheetsServiceBase> _logger;
        private readonly GoogleSheetsDAL _dal;

        protected GoogleSheetsServiceBase(SheetsService service, ILogger <GoogleSheetsServiceBase> logger, GoogleSheetsDAL dal)
        {
            _service = service;
            _logger = logger;
            _dal = dal;
        }

        protected async Task<ValueRange> GetRangeValuesAsync(string spreadsheetId, string range)
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
        }

        protected async Task UpdateRangeValuesAsync(string spreadsheetId, string range, ValueRange valueRange)
        {
            try
            {
                var updateRequest = _service.Spreadsheets.Values.Update(valueRange, spreadsheetId, range);
                updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
                await updateRequest.ExecuteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating range values for {spreadsheetId}, range {range}");
                throw;
            }
        }

        protected async Task<int> CreateTabIfNotExistsAsync(string spreadsheetId, string tabName, long channelId)
        {
            try
            {
                var spreadsheet = await _service.Spreadsheets.Get(spreadsheetId).ExecuteAsync();

                // Check if tab already exists
                foreach (var sheet in spreadsheet.Sheets)
                {
                    if (sheet.Properties.Title == tabName)
                    {
                        return sheet.Properties.SheetId.Value;
                    }
                }

                // Create new tab
                var random = new Random();
                int sheetId = random.Next(1000, 999999);

                var request = new Request
                {
                    AddSheet = new AddSheetRequest
                    {
                        Properties = new SheetProperties
                        {
                            Title = tabName,
                            SheetId = sheetId
                        }
                    }
                };

                var batchRequest = new BatchUpdateSpreadsheetRequest
                {
                    Requests = new List<Request> { request }
                };

                await _service.Spreadsheets.BatchUpdate(batchRequest, spreadsheetId).ExecuteAsync();

                await _dal.SaveTabId(channelId, sheetId);

                return sheetId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating tab {tabName} in spreadsheet {spreadsheetId}");
                throw;
            }
        }
    }
}
