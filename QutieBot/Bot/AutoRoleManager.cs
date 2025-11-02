using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using QutieDAL.DAL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QutieBot.Bot
{
    /// <summary>
    /// Manages automatic role assignment for new members
    /// </summary>
    public class AutoRoleManager
    {
        private readonly AutoRoleDAL _autoRoleDAL;
        private readonly ILogger<AutoRoleManager> _logger;

        public AutoRoleManager(
            AutoRoleDAL autoRoleDAL,
            ILogger<AutoRoleManager> logger)
        {
            _autoRoleDAL = autoRoleDAL ?? throw new ArgumentNullException(nameof(autoRoleDAL));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Assigns all configured auto-roles to a new member
        /// </summary>
        public async Task AssignAutoRolesAsync(DiscordClient client, GuildMemberAddedEventArgs e)
        {
            try
            {
                _logger.LogInformation($"Assigning auto-roles to new member {e.Member.Username} ({e.Member.Id})");

                // Get all configured auto-roles
                var autoRoles = await _autoRoleDAL.GetAllAutoRolesAsync();

                if (autoRoles == null || autoRoles.Count == 0)
                {
                    _logger.LogInformation("No auto-roles configured, skipping role assignment");
                    return;
                }

                _logger.LogInformation($"Found {autoRoles.Count} auto-roles to assign");

                var guild = e.Guild;
                var member = e.Member;
                int successCount = 0;
                int failCount = 0;

                foreach (var autoRole in autoRoles)
                {
                    try
                    {
                        var role = await guild.GetRoleAsync((ulong)autoRole.RoleId);
                        
                        if (role == null)
                        {
                            _logger.LogWarning($"Role {autoRole.RoleId} ({autoRole.RoleName}) not found in guild, skipping");
                            failCount++;
                            continue;
                        }

                        // Check if member already has the role (shouldn't happen for new members, but just in case)
                        if (member.Roles.Any(r => r.Id == role.Id))
                        {
                            _logger.LogInformation($"Member {member.Id} already has role {role.Name}, skipping");
                            continue;
                        }

                        await member.GrantRoleAsync(role, "Auto-role assignment for new member");
                        _logger.LogInformation($"Assigned role {role.Name} ({role.Id}) to {member.Username}");
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error assigning role {autoRole.RoleId} to member {member.Id}");
                        failCount++;
                    }
                }

                _logger.LogInformation($"Auto-role assignment complete for {member.Username}: {successCount} succeeded, {failCount} failed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in auto-role assignment for member {e.Member.Id}");
            }
        }
    }
}
