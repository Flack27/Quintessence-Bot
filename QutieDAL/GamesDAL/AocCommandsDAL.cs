using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QutieDTO.Models;

namespace QutieDAL.GamesDAL
{
    public class AocCommandsDAL
    {
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<AocCommandsDAL> _logger;

        public AocCommandsDAL(
            IDbContextFactory<QutieDataTestContext> contextFactory,
            ILogger<AocCommandsDAL> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<AocData?> GetAoCDataAsync(ulong userId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                return await context.AocData.FirstOrDefaultAsync(data => data.UserId == (long)userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving AoC data for user {userId}");
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

        public async Task SaveOrUpdateAoCDataAsync(AocData gameData)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();

                var existingData = await context.AocData.FirstOrDefaultAsync(data => data.UserId == gameData.UserId);
                if (existingData != null)
                {
                    _logger.LogInformation($"Updating existing AoC data for user {gameData.UserId}");

                    if (!string.IsNullOrEmpty(gameData.IGN)) existingData.IGN = gameData.IGN;
                    if (gameData.Level.HasValue) existingData.Level = gameData.Level;
                    if (!string.IsNullOrEmpty(gameData.Class)) existingData.Class = gameData.Class;
                    if (!string.IsNullOrEmpty(gameData.Role)) existingData.Role = gameData.Role;
                    if (!string.IsNullOrEmpty(gameData.Playstyle)) existingData.Playstyle = gameData.Playstyle;
                    if (!string.IsNullOrEmpty(gameData.PrimaryProfession)) existingData.PrimaryProfession = gameData.PrimaryProfession;
                    if (!string.IsNullOrEmpty(gameData.PrimaryTier)) existingData.PrimaryTier = gameData.PrimaryTier;
                    if (!string.IsNullOrEmpty(gameData.SecondaryProfession)) existingData.SecondaryProfession = gameData.SecondaryProfession;
                    if (!string.IsNullOrEmpty(gameData.SecondaryTier)) existingData.SecondaryTier = gameData.SecondaryTier;
                    if (!string.IsNullOrEmpty(gameData.TertiaryProfession)) existingData.TertiaryProfession = gameData.TertiaryProfession;
                    if (!string.IsNullOrEmpty(gameData.TertiaryTier)) existingData.TertiaryTier = gameData.TertiaryTier;
                }
                else
                {
                    _logger.LogInformation($"Creating new AoC data entry for user {gameData.UserId}");
                    context.AocData.Add(gameData);
                }

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving AoC data for user {gameData.UserId}");
            }
        }
    }
}