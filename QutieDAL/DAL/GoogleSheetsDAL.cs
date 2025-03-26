using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QutieDTO;
using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieDAL.DAL
{
    public class GoogleSheetsDAL
    {
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<GoogleSheetsDAL> _logger;

        public GoogleSheetsDAL(IDbContextFactory<QutieDataTestContext> contextFactory, ILogger<GoogleSheetsDAL> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<List<Game>?> GetGameData()
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var games = await context.Games.Include(g => g.Channels).ToListAsync();
                return games;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching game data");
                return null;
            }
        }

        public async Task<Channel?> GetChannel(long channelId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var channel = await context.Channels
                    .Include(c => c.Role)
                    .Include(c => c.Game)
                    .FirstOrDefaultAsync(c => c.ChannelId == channelId);

                return channel;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting channel {channelId}");
                return null;
            }
        }

        public async Task<bool> SaveTabId(long channelId, int tabId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var channel = await context.Channels.FirstOrDefaultAsync(c => c.ChannelId == channelId);

                if (channel != null)
                {
                    channel.SheetTabId = tabId;
                    await context.SaveChangesAsync();
                    return true;
                }

                _logger.LogWarning($"Channel with ID {channelId} not found when trying to save tab ID");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving tab ID {tabId} for channel {channelId}");
                return false;
            }
        }
    }
}