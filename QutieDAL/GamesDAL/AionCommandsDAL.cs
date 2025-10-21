using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QutieDTO.Models;

namespace QutieDAL.GamesDAL
{
    public class AionCommandsDAL
    {
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<AionCommandsDAL> _logger;

        public AionCommandsDAL(
            IDbContextFactory<QutieDataTestContext> contextFactory,
            ILogger<AionCommandsDAL> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<AionData?> GetAionDataAsync(ulong userId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                return await context.AionData.FirstOrDefaultAsync(data => data.UserId == (long)userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving AION data for user {userId}");
                return null;
            }
        }

        public async Task<List<long?>?> GetRoster(long gameId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var game = await context.Games.Include(g => g.Channels).FirstOrDefaultAsync(g => g.GameId == gameId);

                if (game == null)
                {
                    _logger.LogWarning($"Game with ID {gameId} not found");
                    return null;
                }

                return game.Channels.Select(u => u.RoleId).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving roster for game {gameId}");
                return null;
            }
        }

        public async Task<Game?> GetGameRoles(long gameId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                return await context.Games.FirstOrDefaultAsync(g => g.GameId == gameId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving game roles for game {gameId}");
                return null;
            }
        }

        public async Task SaveOrUpdateAionDataAsync(AionData gameData)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();

                var existingData = await context.AionData.FirstOrDefaultAsync(data => data.UserId == gameData.UserId);
                if (existingData != null)
                {
                    _logger.LogInformation($"Updating existing AION data for user {gameData.UserId}");

                    if (!string.IsNullOrEmpty(gameData.IGN)) existingData.IGN = gameData.IGN;
                    if (gameData.Gearscore.HasValue) existingData.Gearscore = gameData.Gearscore;
                    if (!string.IsNullOrEmpty(gameData.Class)) existingData.Class = gameData.Class;
                    if (!string.IsNullOrEmpty(gameData.Role)) existingData.Role = gameData.Role;
                }
                else
                {
                    _logger.LogInformation($"Creating new AION data entry for user {gameData.UserId}");
                    context.AionData.Add(gameData);
                }

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving AION data for user {gameData.UserId}");
            }
        }
    }
}