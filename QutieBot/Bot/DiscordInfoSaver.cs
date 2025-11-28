using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QutieBot.Bot.Services;
using QutieDAL.DAL;
using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QutieBot.Bot
{
    public class DiscordInfoSaver
    {
        private readonly DiscordInfoSaverDAL _dal;
        private readonly GoogleSheetsFacade _sheets;
        private readonly ILogger<DiscordInfoSaver> _logger;

        // Channel IDs for member count tracking
        private const ulong MemberCountChannelId = 1138138805380067391;
        private const ulong MainRosterChannelId = 1137828721764618340;
        private const ulong MainRosterRoleId = 1137817925638684802;

        public DiscordInfoSaver(
            DiscordInfoSaverDAL dal,
            AutomatedCheckService automated,
            GoogleSheetsFacade sheets,
            RaidHelperManager raidHelper,
            ILogger<DiscordInfoSaver> logger)
        {
            _dal = dal ?? throw new ArgumentNullException(nameof(dal));
            _sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task Client_GuildAvailable(DiscordClient client, GuildAvailableEventArgs e)
        {
            try
            {
                _logger.LogInformation($"Guild {e.Guild.Id} ({e.Guild.Name}) is now available");

                await InitiateUsers(e.Guild);
                await InitiateChannels(e.Guild);
                await InitiateRoles(e.Guild);

                _logger.LogInformation("Syncing user data with Google Sheets");
                await _sheets.SyncUserDataAsync();

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during guild initialization for {e.Guild.Id}");
            }
        }

        private async Task InitiateUsers(DiscordGuild guild)
        {
            try
            {
                _logger.LogInformation($"Initiating users for guild {guild.Id}");

                var users = _dal.GetAllUsers();
                var currentMembers = await guild.GetAllMembersAsync().ToListAsync();

                _logger.LogInformation($"Found {users.Count} users in database and {currentMembers.Count} current members in guild");

                List<User> usersList = new List<User>();
                var userIdsInDatabase = new HashSet<ulong>(users.Select(u => (ulong)u.UserId));

                // Update existing users
                foreach (var user in users)
                {
                    var member = currentMembers.FirstOrDefault(m => m.Id == (ulong)user.UserId);
                    if (member is null)
                    {
                        _logger.LogDebug($"User {user.UserId} is no longer in the guild, marking as inactive");
                        user.InGuild = false;
                        user.Roles.Clear();
                        usersList.Add(user);
                    }
                    else
                    {
                        usersList.Add(GetUser(member));
                    }
                }

                // Add new members
                var newMembers = currentMembers.Where(m => !userIdsInDatabase.Contains(m.Id));
                _logger.LogInformation($"Adding {newMembers.Count()} new members to database");

                foreach (var newMember in newMembers)
                {
                    usersList.Add(GetUser(newMember));
                }

                await _dal.SaveUserDataBulk(usersList);
                _logger.LogInformation("User data saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user initialization");
            }
        }

        private async Task InitiateRoles(DiscordGuild guild)
        {
            try
            {
                _logger.LogInformation($"Initiating roles for guild {guild.Id}");

                var roles = _dal.GetAllRoles();
                var currentRoles = await guild.GetRolesAsync();

                _logger.LogInformation($"Found {roles.Count} roles in database and {currentRoles.Count} current roles in guild");

                var roleIdsInDatabase = new HashSet<ulong>(roles.Select(u => (ulong)u.RoleId));

                // Update existing roles
                foreach (var role in roles)
                {
                    var discordRole = currentRoles.FirstOrDefault(r => r.Id == (ulong)role.RoleId);
                    if (discordRole is null)
                    {
                        _logger.LogDebug($"Role {role.RoleId} ({role.RoleName}) no longer exists, removing from database");
                        await _dal.ClearRole(new Role { RoleId = role.RoleId });
                    }
                    else
                    {
                        var updatedRole = new Role
                        {
                            RoleId = (long)discordRole.Id,
                            RoleName = discordRole.Name
                        };
                        await _dal.SaveRole(updatedRole);
                    }
                }

                // Add new roles
                var newRoles = currentRoles.Where(m => !roleIdsInDatabase.Contains(m.Id));
                _logger.LogInformation($"Adding {newRoles.Count()} new roles to database");

                foreach (var newRole in newRoles)
                {
                    var updatedRole = new Role
                    {
                        RoleId = (long)newRole.Id,
                        RoleName = newRole.Name
                    };
                    await _dal.SaveRole(updatedRole);
                }

                _logger.LogInformation("Role data saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during role initialization");
            }
        }

        private async Task InitiateChannels(DiscordGuild guild)
        {
            try
            {
                _logger.LogInformation($"Initiating channels for guild {guild.Id}");

                var channels = _dal.GetAllChannels();
                var currentChannels = await guild.GetChannelsAsync();
                var currentTextChannels = currentChannels.Where(c => c.Type == DiscordChannelType.Text);

                _logger.LogInformation($"Found {channels.Count} channels in database and {currentTextChannels.Count()} current text channels in guild");

                var channelIdsInDatabase = new HashSet<ulong>(channels.Select(u => (ulong)u.ChannelId));

                // Update existing channels
                foreach (var channel in channels)
                {
                    var discordChannel = currentTextChannels.FirstOrDefault(c => c.Id == (ulong)channel.ChannelId);
                    if (discordChannel is null)
                    {
                        _logger.LogDebug($"Channel {channel.ChannelId} ({channel.ChannelName}) no longer exists, removing from database");
                        await _dal.ClearChannel(new Channel { ChannelId = channel.ChannelId });
                    }
                    else
                    {
                        var updatedChannel = new Channel
                        {
                            ChannelId = (long)discordChannel.Id,
                            ChannelName = discordChannel.Name
                        };
                        await _dal.SaveChannel(updatedChannel);
                    }
                }

                // Add new channels
                var newChannels = currentTextChannels.Where(m => !channelIdsInDatabase.Contains(m.Id));
                _logger.LogInformation($"Adding {newChannels.Count()} new channels to database");

                foreach (var newChannel in newChannels)
                {
                    var updatedChannel = new Channel
                    {
                        ChannelId = (long)newChannel.Id,
                        ChannelName = newChannel.Name
                    };
                    await _dal.SaveChannel(updatedChannel);
                }

                _logger.LogInformation("Channel data saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during channel initialization");
            }
        }

        //Roles
        public async Task Client_GuildRoleCreated(DiscordClient client, GuildRoleCreatedEventArgs e)
        {
            try
            {
                _logger.LogInformation($"Role created: {e.Role.Id} ({e.Role.Name})");

                var role = new Role
                {
                    RoleId = (long)e.Role.Id,
                    RoleName = e.Role.Name,
                };

                await _dal.SaveRole(role);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving created role {e.Role.Id}");
            }
        }

        public async Task Client_GuildRoleUpdated(DiscordClient client, GuildRoleUpdatedEventArgs e)
        {
            try
            {
                _logger.LogInformation($"Role updated: {e.RoleAfter.Id} ({e.RoleBefore.Name} -> {e.RoleAfter.Name})");

                var role = new Role
                {
                    RoleId = (long)e.RoleAfter.Id,
                    RoleName = e.RoleAfter.Name,
                };

                await _dal.SaveRole(role);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving updated role {e.RoleAfter.Id}");
            }
        }

        public async Task Client_GuildRoleDeleted(DiscordClient client, GuildRoleDeletedEventArgs e)
        {
            try
            {
                _logger.LogInformation($"Role deleted: {e.Role.Id} ({e.Role.Name})");

                var role = new Role
                {
                    RoleId = (long)e.Role.Id,
                    RoleName = e.Role.Name,
                };

                await _dal.ClearRole(role);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing deleted role {e.Role.Id}");
            }
        }

        //Users
        public async Task Client_GuildMemberAdded(DiscordClient client, GuildMemberAddedEventArgs e)
        {
            try
            {
                _logger.LogInformation($"Member added to guild: {e.Member.Id} ({e.Member.DisplayName})");
                await _dal.SaveUserData(GetUser(e.Member));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving new member {e.Member.Id}");
            }
        }

        public async Task Client_GuildMemberUpdated(DiscordClient client, GuildMemberUpdatedEventArgs e)
        {
            try
            {
                _logger.LogInformation($"Member updated: {e.MemberAfter.Id} ({e.MemberAfter.DisplayName})");

                // Check for role changes for logging purposes
                var rolesBefore = e.RolesBefore.Select(r => r.Name).OrderBy(n => n);
                var rolesAfter = e.RolesAfter.Select(r => r.Name).OrderBy(n => n);

                if (!rolesBefore.SequenceEqual(rolesAfter))
                {
                    var addedRoles = rolesAfter.Except(rolesBefore);
                    var removedRoles = rolesBefore.Except(rolesAfter);

                    if (addedRoles.Any())
                        _logger.LogInformation($"Roles added to {e.MemberAfter.DisplayName}: {string.Join(", ", addedRoles)}");

                    if (removedRoles.Any())
                        _logger.LogInformation($"Roles removed from {e.MemberAfter.DisplayName}: {string.Join(", ", removedRoles)}");
                }

                var user = new User
                {
                    UserId = (long)e.MemberAfter.Id,
                    UserName = e.MemberAfter.Username,
                    DisplayName = e.MemberAfter.DisplayName,
                    Avatar = e.MemberAfter.AvatarUrl,
                    InGuild = true,
                    Roles = e.MemberAfter.Roles?.Select(role => new Role { RoleId = (long)role.Id, RoleName = role.Name }).ToList() ?? new List<Role>()
                };

                await _dal.SaveUserData(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving updated member {e.MemberAfter.Id}");
            }
        }

        public async Task Client_GuildMemberRemoved(DiscordClient client, GuildMemberRemovedEventArgs e)
        {
            try
            {
                _logger.LogInformation($"Member left guild: {e.Member.Id} ({e.Member.DisplayName})");

                var user = new User
                {
                    UserId = (long)e.Member.Id,
                    UserName = e.Member.Username,
                    DisplayName = e.Member.DisplayName,
                    Avatar = e.Member.AvatarUrl,
                    InGuild = false,
                    Roles = new List<Role>()
                };

                await _dal.SaveUserData(user);

                // Remove user's reactions if they left the server
                await RemoveUserReactions(client, e.Member.Id, e.Guild);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing member removal {e.Member.Id}");
            }
        }

        // Method to remove all reactions from a user who left the server
        private async Task RemoveUserReactions(DiscordClient client, ulong userId, DiscordGuild guild)
        {
            try
            {
                _logger.LogInformation($"Removing reactions for user {userId} who left the server");

                // Get all reaction roles in the database
                var reactionRoles = await _dal.GetAllReactionRoles();
                if (reactionRoles == null || !reactionRoles.Any())
                {
                    _logger.LogInformation("No reaction roles found to process");
                    return;
                }

                int removedCount = 0;

                // Group by channel and message to reduce API calls
                var messageGroups = reactionRoles
                    .GroupBy(r => new { ChannelId = r.ChannelId, MessageId = r.MessageId })
                    .ToList();

                foreach (var group in messageGroups)
                {
                    try
                    {
                        // Get the channel
                        var channelId = (ulong)group.Key.ChannelId;
                        if (!guild.Channels.TryGetValue(channelId, out var channel))
                        {
                            _logger.LogWarning($"Channel {channelId} not found when removing reactions");
                            continue;
                        }

                        // Get the message
                        var messageId = (ulong)group.Key.MessageId;
                        DiscordMessage message;
                        try
                        {
                            message = await channel.GetMessageAsync(messageId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Message {messageId} not found in channel {channelId}");
                            continue;
                        }

                        // Process each reaction
                        foreach (var reaction in message.Reactions)
                        {
                            try
                            {
                                // Get all users who reacted with this emoji
                                var reactors = await message.GetReactionsAsync(reaction.Emoji).ToListAsync();

                                // Check if the user who left had this reaction
                                if (reactors.Any(r => r.Id == userId))
                                {
                                    // Remove the reaction
                                    await message.DeleteReactionAsync(reaction.Emoji, client.GetUserAsync(userId).Result);
                                    removedCount++;

                                    _logger.LogDebug($"Removed reaction {reaction.Emoji.Name} from message {messageId} for user {userId}");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, $"Failed to remove reaction {reaction.Emoji.Name} for user {userId}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Error processing message group for channel {group.Key.ChannelId}, message {group.Key.MessageId}");
                    }
                }

                _logger.LogInformation($"Removed {removedCount} reactions for user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing reactions for user {userId}");
            }
        }

        //Channels
        public async Task Client_ChannelCreated(DiscordClient client, ChannelCreatedEventArgs e)
        {
            try
            {
                if (e.Channel.Type == DiscordChannelType.Text)
                {
                    _logger.LogInformation($"Text channel created: {e.Channel.Id} ({e.Channel.Name})");

                    var channel = new Channel
                    {
                        ChannelId = (long)e.Channel.Id,
                        ChannelName = e.Channel.Name,
                    };

                    await _dal.SaveChannel(channel);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving created channel {e.Channel.Id}");
            }
        }

        public async Task Client_ChannelUpdated(DiscordClient client, ChannelUpdatedEventArgs e)
        {
            try
            {
                if (e.ChannelAfter.Type == DiscordChannelType.Text)
                {
                    _logger.LogInformation($"Text channel updated: {e.ChannelAfter.Id} ({e.ChannelBefore.Name} -> {e.ChannelAfter.Name})");

                    var channel = new Channel
                    {
                        ChannelId = (long)e.ChannelAfter.Id,
                        ChannelName = e.ChannelAfter.Name,
                    };

                    await _dal.SaveChannel(channel);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving updated channel {e.ChannelAfter.Id}");
            }
        }

        public async Task Client_ChannelDeleted(DiscordClient client, ChannelDeletedEventArgs e)
        {
            try
            {
                if (e.Channel.Type == DiscordChannelType.Text)
                {
                    _logger.LogInformation($"Text channel deleted: {e.Channel.Id} ({e.Channel.Name})");

                    var channel = new Channel
                    {
                        ChannelId = (long)e.Channel.Id,
                        ChannelName = e.Channel.Name,
                    };

                    await _dal.ClearChannel(channel);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing deleted channel {e.Channel.Id}");
            }
        }

        public async Task UpdateMemberCountChannelName(DiscordGuild guild)
        {
            try
            {
                _logger.LogInformation($"Updating member count channels for guild {guild.Id}");

                int memberCount = guild.MemberCount;
                int mainRosterCount = 0;

                // Update total member count channel
                if (guild.Channels.TryGetValue(MemberCountChannelId, out var memberChannel) && memberChannel is DiscordChannel memberVoiceChannel)
                {
                    await memberVoiceChannel.ModifyAsync(properties =>
                    {
                        properties.Name = $"Members: {memberCount}";
                    });

                    _logger.LogInformation($"Updated member count channel to show {memberCount} members");
                }
                else
                {
                    _logger.LogWarning($"Member count channel {MemberCountChannelId} not found");
                }

                // Count members with main roster role
                foreach (var member in guild.Members.Values)
                {
                    if (member.Roles.Any(role => role.Id == MainRosterRoleId))
                    {
                        mainRosterCount++;
                    }
                }

                // Update main roster count channel
                if (guild.Channels.TryGetValue(MainRosterChannelId, out var rosterChannel) && rosterChannel is DiscordChannel rosterVoiceChannel)
                {
                    await rosterVoiceChannel.ModifyAsync(properties =>
                    {
                        properties.Name = $"Main-Roster: {mainRosterCount}";
                    });

                    _logger.LogInformation($"Updated main roster count channel to show {mainRosterCount} members");
                }
                else
                {
                    _logger.LogWarning($"Main roster count channel {MainRosterChannelId} not found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating member count channels");
            }
        }

        private User GetUser(DiscordMember member)
        {
            // Initialize with default values for new users
            var userData = new UserData
            {
                MessageXp = 0,
                MessageLevel = 0,
                MessageRequiredXp = 50,
                MessageCount = 0,
                VoiceXp = 0,
                VoiceLevel = 0,
                VoiceRequiredXp = 50,
                TotalVoiceTime = 0,
                StoredMessageXp = 0,
                StoredVoiceXp = 0,
                Karma = 1
            };

            var user = new User
            {
                UserId = (long)member.Id,
                UserName = member.Username,
                DisplayName = member.DisplayName,
                Avatar = member.AvatarUrl,
                InGuild = true,
                UserData = userData,
                Roles = member.Roles?.Select(role => new Role { RoleId = (long)role.Id, RoleName = role.Name }).ToList() ?? new List<Role>()
            };

            return user;
        }
    }
}