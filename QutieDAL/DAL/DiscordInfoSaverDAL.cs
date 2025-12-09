using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QutieDAL.DAL
{
    public class DiscordInfoSaverDAL
    {
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<DiscordInfoSaverDAL> _logger;

        public DiscordInfoSaverDAL(
            IDbContextFactory<QutieDataTestContext> contextFactory,
            ILogger<DiscordInfoSaverDAL> logger)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves all users from the database with their roles
        /// </summary>
        public List<User> GetAllUsers()
        {
            try
            {
                _logger.LogInformation("Retrieving all users from the database");

                using var context = _contextFactory.CreateDbContext();
                var users = context.Users
                    .Include(u => u.Roles)
                    .Include(u => u.UserData)
                    .ToList();

                _logger.LogInformation($"Retrieved {users.Count} users from the database");
                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all users from the database");
                return new List<User>();
            }
        }

        /// <summary>
        /// Retrieves all roles from the database
        /// </summary>
        public List<Role> GetAllRoles()
        {
            try
            {
                _logger.LogInformation("Retrieving all roles from the database");

                using var context = _contextFactory.CreateDbContext();
                var roles = context.Roles.ToList();

                _logger.LogInformation($"Retrieved {roles.Count} roles from the database");
                return roles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all roles from the database");
                return new List<Role>();
            }
        }

        /// <summary>
        /// Retrieves all channels from the database
        /// </summary>
        public List<Channel> GetAllChannels()
        {
            try
            {
                _logger.LogInformation("Retrieving all channels from the database");

                using var context = _contextFactory.CreateDbContext();
                var channels = context.Channels.ToList();

                _logger.LogInformation($"Retrieved {channels.Count} channels from the database");
                return channels;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all channels from the database");
                return new List<Channel>();
            }
        }

        /// <summary>
        /// Saves a batch of users to the database, updating existing users and adding new ones
        /// </summary>
        public async Task SaveUserDataBulk(List<User> users)
        {
            if (users == null || !users.Any())
            {
                _logger.LogWarning("SaveUserDataBulk called with empty or null user list");
                return;
            }

            _logger.LogInformation($"Saving bulk user data for {users.Count} users");

            try
            {
                using var context = _contextFactory.CreateDbContext();
                var roleCache = context.Roles.ToDictionary(r => r.RoleId);
                int updatedCount = 0;
                int newCount = 0;

                foreach (var user in users)
                {
                    try
                    {
                        var existingUser = await context.Users
                            .Include(u => u.Roles)
                            .Include(u => u.UserData)
                            .FirstOrDefaultAsync(u => u.UserId == user.UserId);

                        if (existingUser != null)
                        {
                            // Update existing user
                            existingUser.UserName = user.UserName;
                            existingUser.DisplayName = user.DisplayName;
                            existingUser.Avatar = user.Avatar;
                            existingUser.InGuild = user.InGuild;

                            // Update roles
                            var existingRoleIds = existingUser.Roles.Select(r => r.RoleId).ToHashSet();
                            var newRoleIds = user.Roles.Select(r => r.RoleId).ToHashSet();

                            // Remove roles that are no longer assigned
                            var rolesToRemove = existingUser.Roles.Where(r => !newRoleIds.Contains(r.RoleId)).ToList();
                            foreach (var role in rolesToRemove)
                            {
                                existingUser.Roles.Remove(role);
                            }

                            // Add newly assigned roles
                            var rolesToAdd = user.Roles.Where(r => !existingRoleIds.Contains(r.RoleId)).ToList();
                            foreach (var role in rolesToAdd)
                            {
                                if (!roleCache.TryGetValue(role.RoleId, out var trackedRole))
                                {
                                    context.Roles.Add(role);
                                    roleCache[role.RoleId] = role;
                                    trackedRole = role;
                                }
                                existingUser.Roles.Add(trackedRole);
                            }

                            // Ensure user data exists
                            if (existingUser.UserData == null && user.UserData != null)
                            {
                                existingUser.UserData = user.UserData;
                            }

                            updatedCount++;
                        }
                        else
                        {
                            // Add new user
                            var trackedRoles = user.Roles.Select(role =>
                            {
                                if (!roleCache.TryGetValue(role.RoleId, out var trackedRole))
                                {
                                    context.Roles.Add(role);
                                    roleCache[role.RoleId] = role;
                                    trackedRole = role;
                                }
                                return trackedRole;
                            }).ToList();

                            user.Roles = trackedRoles;
                            context.Users.Add(user);
                            newCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing user {user.UserId} ({user.UserName}) during bulk save");
                        // Continue with other users even if one fails
                    }
                }

                // Save all changes at once
                int changes = await context.SaveChangesAsync();
                _logger.LogInformation($"Bulk user save completed: {updatedCount} users updated, {newCount} users added, {changes} total changes");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bulk user save operation");
                throw; // Rethrow to allow calling code to handle the exception
            }
        }

        /// <summary>
        /// Saves or updates a single user in the database
        /// </summary>
        public async Task SaveUserData(User user)
        {
            if (user == null)
            {
                _logger.LogWarning("SaveUserData called with null user");
                return;
            }

            _logger.LogInformation($"Saving user data for user {user.UserId} ({user.UserName})");

            try
            {
                using var context = _contextFactory.CreateDbContext();
                var existingUser = await context.Users
                    .Include(u => u.UserData)
                    .Include(u => u.Roles)
                    .FirstOrDefaultAsync(u => u.UserId == user.UserId);

                var roleCache = context.Roles.ToDictionary(r => r.RoleId);

                if (existingUser == null)
                {
                    // Add new user
                    _logger.LogInformation($"Adding new user {user.UserId} ({user.UserName})");
                    context.Users.Add(user);
                }
                else
                {
                    // Update existing user, only changing non-null values
                    if (user.UserName != null) existingUser.UserName = user.UserName;
                    if (user.DisplayName != null) existingUser.DisplayName = user.DisplayName;
                    if (user.Avatar != null) existingUser.Avatar = user.Avatar;
                    if (user.InGuild != null) existingUser.InGuild = user.InGuild;

                    // Update roles if provided
                    if (user.Roles != null)
                    {
                        var existingRoleIds = existingUser.Roles.Select(r => r.RoleId).ToHashSet();
                        var newRoleIds = user.Roles.Select(r => r.RoleId).ToHashSet();

                        // Remove roles that are no longer assigned
                        var rolesToRemove = existingUser.Roles.Where(r => !newRoleIds.Contains(r.RoleId)).ToList();
                        foreach (var role in rolesToRemove)
                        {
                            existingUser.Roles.Remove(role);
                        }

                        // Add newly assigned roles
                        var rolesToAdd = user.Roles.Where(r => !existingRoleIds.Contains(r.RoleId)).ToList();
                        foreach (var role in rolesToAdd)
                        {
                            if (!roleCache.TryGetValue(role.RoleId, out var trackedRole))
                            {
                                context.Roles.Add(role);
                                roleCache[role.RoleId] = role;
                                trackedRole = role;
                            }
                            existingUser.Roles.Add(trackedRole);
                        }
                    }

                    // Ensure user data exists
                    if (existingUser.UserData == null && user.UserData != null)
                    {
                        existingUser.UserData = user.UserData;
                    }
                }

                await context.SaveChangesAsync();
                _logger.LogInformation($"Successfully saved user data for {user.UserId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving user data for user {user.UserId}");
                throw; // Rethrow to allow calling code to handle the exception
            }
        }

        /// <summary>
        /// Saves or updates a role in the database
        /// </summary>
        public async Task SaveRole(Role role)
        {
            if (role == null)
            {
                _logger.LogWarning("SaveRole called with null role");
                return;
            }

            _logger.LogInformation($"Saving role {role.RoleId} ({role.RoleName})");

            try
            {
                using var context = _contextFactory.CreateDbContext();
                var existingRole = await context.Roles.FirstOrDefaultAsync(r => r.RoleId == role.RoleId);

                if (existingRole != null)
                {
                    // Update existing role
                    existingRole.RoleName = role.RoleName;
                    _logger.LogDebug($"Updated existing role {role.RoleId} ({role.RoleName})");
                }
                else
                {
                    // Add new role
                    context.Roles.Add(role);
                    _logger.LogDebug($"Added new role {role.RoleId} ({role.RoleName})");
                }

                await context.SaveChangesAsync();
                _logger.LogInformation($"Successfully saved role {role.RoleId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving role {role.RoleId}");
                throw; // Rethrow to allow calling code to handle the exception
            }
        }

        /// <summary>
        /// Saves or updates a channel in the database
        /// </summary>
        public async Task SaveChannel(Channel channel)
        {
            if (channel == null)
            {
                _logger.LogWarning("SaveChannel called with null channel");
                return;
            }

            _logger.LogInformation($"Saving channel {channel.ChannelId} ({channel.ChannelName})");

            try
            {
                using var context = _contextFactory.CreateDbContext();
                var existingChannel = await context.Channels.FirstOrDefaultAsync(c => c.ChannelId == channel.ChannelId);

                if (existingChannel != null)
                {
                    // Update existing channel
                    existingChannel.ChannelName = channel.ChannelName;
                    _logger.LogDebug($"Updated existing channel {channel.ChannelId} ({channel.ChannelName})");
                }
                else
                {
                    // Add new channel
                    context.Channels.Add(channel);
                    _logger.LogDebug($"Added new channel {channel.ChannelId} ({channel.ChannelName})");
                }

                await context.SaveChangesAsync();
                _logger.LogInformation($"Successfully saved channel {channel.ChannelId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving channel {channel.ChannelId}");
                throw; // Rethrow to allow calling code to handle the exception
            }
        }

        /// <summary>
        /// Removes a role from the database
        /// </summary>
        public async Task ClearRole(Role role)
        {
            if (role == null || role.RoleId <= 0)
            {
                _logger.LogWarning("ClearRole called with null or invalid role");
                return;
            }

            _logger.LogInformation($"Removing role {role.RoleId} ({role.RoleName})");

            try
            {
                using var context = _contextFactory.CreateDbContext();

                // Find the role to remove
                var existingRole = await context.Roles
                    .FirstOrDefaultAsync(r => r.RoleId == role.RoleId);

                if (existingRole != null)
                {
                    // Before removing the role, we need to ensure it's removed from all users
                    var usersWithRole = await context.Users
                        .Include(u => u.Roles)
                        .Where(u => u.Roles.Any(r => r.RoleId == role.RoleId))
                        .ToListAsync();

                    if (usersWithRole.Any())
                    {
                        _logger.LogInformation($"Removing role {role.RoleId} from {usersWithRole.Count} users");

                        foreach (var user in usersWithRole)
                        {
                            var roleToRemove = user.Roles.FirstOrDefault(r => r.RoleId == role.RoleId);
                            if (roleToRemove != null)
                            {
                                user.Roles.Remove(roleToRemove);
                            }
                        }
                    }

                    // Now remove the role itself
                    context.Roles.Remove(existingRole);
                    await context.SaveChangesAsync();
                    _logger.LogInformation($"Successfully removed role {role.RoleId}");
                }
                else
                {
                    _logger.LogInformation($"Role {role.RoleId} not found in database, no action taken");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing role {role.RoleId}");
                throw; // Rethrow to allow calling code to handle the exception
            }
        }

        /// <summary>
        /// Removes a channel from the database
        /// </summary>
        public async Task ClearChannel(Channel channel)
        {
            if (channel == null || channel.ChannelId <= 0)
            {
                _logger.LogWarning("ClearChannel called with null or invalid channel");
                return;
            }

            _logger.LogInformation($"Removing channel {channel.ChannelId} ({channel.ChannelName})");

            try
            {
                using var context = _contextFactory.CreateDbContext();

                // Find the channel to remove
                var existingChannel = await context.Channels
                    .FirstOrDefaultAsync(c => c.ChannelId == channel.ChannelId);

                if (existingChannel != null)
                {
                    // Remove the channel
                    context.Channels.Remove(existingChannel);
                    await context.SaveChangesAsync();
                    _logger.LogInformation($"Successfully removed channel {channel.ChannelId}");
                }
                else
                {
                    _logger.LogInformation($"Channel {channel.ChannelId} not found in database, no action taken");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing channel {channel.ChannelId}");
                throw; // Rethrow to allow calling code to handle the exception
            }
        }

        /// <summary>
        /// Gets a user by ID with roles and user data included
        /// </summary>
        public async Task<User> GetUserById(long userId)
        {
            try
            {
                _logger.LogInformation($"Retrieving user {userId} from database");

                using var context = _contextFactory.CreateDbContext();

                var user = await context.Users
                    .Include(u => u.Roles)
                    .Include(u => u.UserData)
                    .FirstOrDefaultAsync(u => u.UserId == userId);

                if (user != null)
                {
                    _logger.LogInformation($"Successfully retrieved user {userId}");
                }
                else
                {
                    _logger.LogInformation($"User {userId} not found in database");
                }

                return user;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving user {userId}");
                return null;
            }
        }

        /// <summary>
        /// Gets a role by ID
        /// </summary>
        public async Task<Role> GetRoleById(long roleId)
        {
            try
            {
                _logger.LogInformation($"Retrieving role {roleId} from database");

                using var context = _contextFactory.CreateDbContext();

                var role = await context.Roles
                    .FirstOrDefaultAsync(r => r.RoleId == roleId);

                if (role != null)
                {
                    _logger.LogInformation($"Successfully retrieved role {roleId}");
                }
                else
                {
                    _logger.LogInformation($"Role {roleId} not found in database");
                }

                return role;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving role {roleId}");
                return null;
            }
        }

        /// <summary>
        /// Gets a channel by ID
        /// </summary>
        public async Task<Channel> GetChannelById(long channelId)
        {
            try
            {
                _logger.LogInformation($"Retrieving channel {channelId} from database");

                using var context = _contextFactory.CreateDbContext();

                var channel = await context.Channels
                    .FirstOrDefaultAsync(c => c.ChannelId == channelId);

                if (channel != null)
                {
                    _logger.LogInformation($"Successfully retrieved channel {channelId}");
                }
                else
                {
                    _logger.LogInformation($"Channel {channelId} not found in database");
                }

                return channel;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving channel {channelId}");
                return null;
            }
        }

        public async Task<List<ReactionRoles>> GetAllReactionRoles()
        {
            try
            {
                _logger.LogInformation("Retrieving all reaction roles");

                using var context = _contextFactory.CreateDbContext();

                var reactionRoles = await context.ReactionRoles.ToListAsync();

                _logger.LogInformation($"Retrieved {reactionRoles.Count} reaction roles");

                return reactionRoles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all reaction roles");
                return new List<ReactionRoles>();
            }
        }

        /// <summary>
        /// Updates the form submission to rejected and adds a note to user's description
        /// </summary>
        public async Task UpdateDatabaseForUserLeft(ulong userId, long submissionId)
        {
            try
            {
                _logger.LogInformation($"Updating database for user {userId} who left with submission {submissionId}");

                // Update FormSubmission to set Approved = false
                using var context = _contextFactory.CreateDbContext();

                var submission = await context.FormSubmissions.FirstOrDefaultAsync(fs => fs.SubmissionId == submissionId);

                if (submission != null)
                {
                    submission.Approved = false;
                    _logger.LogInformation($"Set submission {submissionId} to rejected (Approved = false)");
                }

                // Update user's description
                var user = await context.Users
                    .FirstOrDefaultAsync(u => u.UserId == (long)userId);

                if (user != null)
                {
                    // Add timestamp message to description
                    var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    var leftMessage = $"[{timestamp} UTC] Applied but left server before interview completion";

                    if (string.IsNullOrWhiteSpace(user.Description))
                    {
                        user.Description = leftMessage;
                    }
                    else
                    {
                        user.Description += $"\n{leftMessage}";
                    }

                    _logger.LogInformation($"Updated user {userId} description with left server note");
                }

                await context.SaveChangesAsync();
                _logger.LogInformation($"Database updates completed for user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating database for user {userId} who left");
            }
        }
    }
}