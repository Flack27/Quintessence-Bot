using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QutieDTO.Models;

namespace QutieDAL.GamesDAL
{
    public class WwmCommandsDAL
    {
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<WwmCommandsDAL> _logger;

        public WwmCommandsDAL(
            IDbContextFactory<QutieDataTestContext> contextFactory,
            ILogger<WwmCommandsDAL> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<WwmData?> GetWwmDataAsync(ulong userId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                return await context.WwmData.FirstOrDefaultAsync(data => data.UserId == (long)userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving WWM data for user {userId}");
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

        public async Task SaveOrUpdateWwmDataAsync(WwmData gameData)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();

                var existingData = await context.WwmData.FirstOrDefaultAsync(data => data.UserId == gameData.UserId);
                if (existingData != null)
                {
                    _logger.LogInformation($"Updating existing WWM data for user {gameData.UserId}");

                    if (!string.IsNullOrEmpty(gameData.IGN)) existingData.IGN = gameData.IGN;
                    if (gameData.Level.HasValue) existingData.Level = gameData.Level;
                    if (!string.IsNullOrEmpty(gameData.PrimaryWeapon)) existingData.PrimaryWeapon = gameData.PrimaryWeapon;
                    if (!string.IsNullOrEmpty(gameData.SecondaryWeapon)) existingData.SecondaryWeapon = gameData.SecondaryWeapon;
                    if (!string.IsNullOrEmpty(gameData.Role)) existingData.Role = gameData.Role;
                    if (!string.IsNullOrEmpty(gameData.Playstyle)) existingData.Playstyle = gameData.Playstyle;
                }
                else
                {
                    _logger.LogInformation($"Creating new WWM data entry for user {gameData.UserId}");
                    context.WwmData.Add(gameData);
                }

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving WWM data for user {gameData.UserId}");
            }
        }
    }
}