using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QutieDTO.Models;

namespace QutieDAL.DAL
{
    /// <summary>
    /// Data access layer for reaction role configurations
    /// </summary>
    public class ReactionRoleManagerDAL
    {
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<ReactionRoleManagerDAL> _logger;

        /// <summary>
        /// Initializes a new instance of the ReactionRoleManagerDAL class
        /// </summary>
        /// <param name="contextFactory">The database context factory</param>
        /// <param name="logger">The logger instance</param>
        public ReactionRoleManagerDAL(
            IDbContextFactory<QutieDataTestContext> contextFactory,
            ILogger<ReactionRoleManagerDAL> logger)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves all reaction role configurations from the database
        /// </summary>
        /// <returns>A list of reaction role configurations</returns>
        public async Task<List<ReactionRoleConfig>> GetAllReactionRoleConfigsAsync()
        {
            _logger.LogInformation("Retrieving all reaction role configurations");

            try
            {
                using var context = _contextFactory.CreateDbContext();
                var configs = await context.ReactionRoleConfigs.ToListAsync();

                _logger.LogInformation($"Retrieved {configs.Count} reaction role configurations");
                return configs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving reaction role configurations");
                return new List<ReactionRoleConfig>();
            }
        }
    }
}