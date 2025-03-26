using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace QutieDAL.DAL
{
    public class AutomatedCheckDAL
    {
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<AutomatedCheckDAL> _logger;

        public AutomatedCheckDAL(IDbContextFactory<QutieDataTestContext> context, ILogger<AutomatedCheckDAL> logger)
        {
            _contextFactory = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets all automated check configurations
        /// </summary>
        public async Task<List<AutomatedChecks>> GetAllAutomatedChecks()
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                return await context.AutomatedChecks.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving automated checks");
                return new List<AutomatedChecks>();
            }
        }

        /// <summary>
        /// Marks an event as having been processed by a specific check
        /// </summary>
        public async Task<bool> MarkEventAsProcessed(long eventId, int checkId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var processedCheck = new ProcessedAutomatedCheck
                {
                    EventId = eventId,
                    CheckId = checkId,
                    ProcessedAt = DateTime.UtcNow
                };
                context.ProcessedAutomatedChecks.Add(processedCheck);
                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking event {EventId} as processed by check {CheckId}", eventId, checkId);
                return false;
            }
        }

        /// <summary>
        /// Checks if an event has already been processed by a specific check
        /// </summary>
        public async Task<bool> HasEventBeenProcessed(long eventId, int checkId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                return await context.ProcessedAutomatedChecks
                    .AnyAsync(p => p.EventId == eventId && p.CheckId == checkId);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if event {EventId} has been processed by check {CheckId}", eventId, checkId);
                return false;
            }
        }
    }
}