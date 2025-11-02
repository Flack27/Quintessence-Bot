using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QutieDAL.DAL
{
    /// <summary>
    /// Data access layer for auto-role functionality
    /// </summary>
    public class AutoRoleDAL
    {
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<AutoRoleDAL> _logger;

        public AutoRoleDAL(
            IDbContextFactory<QutieDataTestContext> contextFactory,
            ILogger<AutoRoleDAL> logger)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Adds a new auto-role to the database
        /// </summary>
        public async Task<bool> AddAutoRoleAsync(long roleId, string roleName)
        {
            try
            {
                _logger.LogInformation($"Adding auto-role: {roleName} ({roleId})");

                using var context = _contextFactory.CreateDbContext();

                // Check if role already exists
                var existingRole = await context.AutoRoles
                    .FirstOrDefaultAsync(ar => ar.RoleId == roleId);

                if (existingRole != null)
                {
                    _logger.LogWarning($"Auto-role {roleId} already exists");
                    return false;
                }

                var autoRole = new AutoRole
                {
                    RoleId = roleId,
                    RoleName = roleName,
                    CreatedAt = DateTime.UtcNow
                };

                context.AutoRoles.Add(autoRole);
                await context.SaveChangesAsync();

                _logger.LogInformation($"Successfully added auto-role {roleName} ({roleId})");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding auto-role {roleId}");
                return false;
            }
        }

        /// <summary>
        /// Removes an auto-role from the database
        /// </summary>
        public async Task<bool> RemoveAutoRoleAsync(long roleId)
        {
            try
            {
                _logger.LogInformation($"Removing auto-role: {roleId}");

                using var context = _contextFactory.CreateDbContext();

                var autoRole = await context.AutoRoles
                    .FirstOrDefaultAsync(ar => ar.RoleId == roleId);

                if (autoRole == null)
                {
                    _logger.LogWarning($"Auto-role {roleId} not found");
                    return false;
                }

                context.AutoRoles.Remove(autoRole);
                await context.SaveChangesAsync();

                _logger.LogInformation($"Successfully removed auto-role {roleId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing auto-role {roleId}");
                return false;
            }
        }

        /// <summary>
        /// Gets all auto-roles from the database
        /// </summary>
        public async Task<List<AutoRole>> GetAllAutoRolesAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving all auto-roles");

                using var context = _contextFactory.CreateDbContext();

                var autoRoles = await context.AutoRoles
                    .OrderBy(ar => ar.RoleName)
                    .ToListAsync();

                _logger.LogInformation($"Retrieved {autoRoles.Count} auto-roles");
                return autoRoles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving auto-roles");
                return new List<AutoRole>();
            }
        }

        /// <summary>
        /// Checks if a role is configured as an auto-role
        /// </summary>
        public async Task<bool> IsAutoRoleAsync(long roleId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();

                return await context.AutoRoles
                    .AnyAsync(ar => ar.RoleId == roleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking if role {roleId} is an auto-role");
                return false;
            }
        }
    }
}
