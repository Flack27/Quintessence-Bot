using DSharpPlus.EventArgs;
using DSharpPlus;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QutieDAL.DAL;

namespace QutieBot.Bot
{
    public class ReactionRoleHandler
    {
        private readonly DiscordClient _client;
        private readonly ILogger<ReactionRoleHandler> _logger;
        private readonly ReactionRoleHandlerDAL _reactionRolesDAL;

        public ReactionRoleHandler(
            DiscordClient client,
            ReactionRoleHandlerDAL reactionRolesDAL,
            ILogger<ReactionRoleHandler> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _reactionRolesDAL = reactionRolesDAL ?? throw new ArgumentNullException(nameof(reactionRolesDAL));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task OnReactionAdded(DiscordClient sender, MessageReactionAddedEventArgs e)
        {
            try
            {
                // Ignore reactions from bots
                if (e.User.IsBot)
                    return;

                // Check if this reaction is a configured reaction role
                var reactionRole = await _reactionRolesDAL.GetReactionRole(
                    (long)e.Channel.Id,
                    (long)e.Message.Id,
                    e.Emoji.Name,
                    e.Emoji.Id != 0 ? (long)e.Emoji.Id : 0);

                if (reactionRole == null)
                    return; // Not a configured reaction role

                // Get the guild and member
                var guild = e.Guild;
                var member = await guild.GetMemberAsync(e.User.Id);

                if (member == null)
                {
                    _logger.LogWarning($"Could not find member {e.User.Id} in guild {guild.Id}");
                    return;
                }

                // Get the role
                var role = await guild.GetRoleAsync((ulong)reactionRole.RoleId);
                if (role == null)
                {
                    _logger.LogWarning($"Could not find role {reactionRole.RoleId} in guild {guild.Id}");
                    return;
                }

                // Check if member already has the role
                if (member.Roles.Any(r => r.Id == role.Id))
                {
                    _logger.LogInformation($"Member {member.Id} already has role {role.Id}");
                    return;
                }

                // Add the role
                try
                {
                    await member.GrantRoleAsync(role, $"Reaction Role: {e.Emoji.Name}");
                    _logger.LogInformation($"Granted role {role.Id} to member {member.Id} for emoji {e.Emoji.Name}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to grant role {role.Id} to member {member.Id}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in reaction added handler");
            }
        }

        public async Task OnReactionRemoved(DiscordClient sender, MessageReactionRemovedEventArgs e)
        {
            try
            {
                // Ignore reactions from bots
                if (e.User.IsBot)
                    return;

                // Check if this reaction is a configured reaction role
                var reactionRole = await _reactionRolesDAL.GetReactionRole(
                    (long)e.Channel.Id,
                    (long)e.Message.Id,
                    e.Emoji.Name,
                    e.Emoji.Id != 0 ? (long)e.Emoji.Id : 0);

                if (reactionRole == null)
                    return; // Not a configured reaction role

                // Get the guild and member
                var guild = e.Guild;
                var member = await guild.GetMemberAsync(e.User.Id);

                if (member == null)
                {
                    _logger.LogWarning($"Could not find member {e.User.Id} in guild {guild.Id}");
                    return;
                }

                // Get the role
                var role = await guild.GetRoleAsync((ulong)reactionRole.RoleId);
                if (role == null)
                {
                    _logger.LogWarning($"Could not find role {reactionRole.RoleId} in guild {guild.Id}");
                    return;
                }

                // Check if member has the role
                if (!member.Roles.Any(r => r.Id == role.Id))
                {
                    _logger.LogInformation($"Member {member.Id} doesn't have role {role.Id}");
                    return;
                }

                // Check if member has any other reactions to the same message that grant the same role
                bool hasOtherReactions = false;
                var message = await e.Channel.GetMessageAsync(e.Message.Id);

                foreach (var reaction in message.Reactions)
                {
                    if (reaction.Emoji.Equals(e.Emoji))
                        continue; // Skip the emoji that was just removed

                    // Check if this emoji grants the same role
                    var otherReactionRole = await _reactionRolesDAL.GetReactionRole(
                        (long)e.Channel.Id,
                        (long)e.Message.Id,
                        reaction.Emoji.Name,
                        reaction.Emoji.Id != 0 ? (long)reaction.Emoji.Id : 0);

                    if (otherReactionRole != null && otherReactionRole.RoleId == reactionRole.RoleId)
                    {
                        // Check if user has this reaction
                        var reactors = await message.GetReactionsAsync(reaction.Emoji).ToListAsync();
                        if (reactors.Any(r => r.Id == e.User.Id))
                        {
                            hasOtherReactions = true;
                            break;
                        }
                    }
                }

                if (hasOtherReactions)
                {
                    _logger.LogInformation($"Member {member.Id} has other reactions that grant role {role.Id}, not removing");
                    return;
                }

                // Remove the role
                try
                {
                    await member.RevokeRoleAsync(role, $"Reaction Role: {e.Emoji.Name} removed");
                    _logger.LogInformation($"Revoked role {role.Id} from member {member.Id} for emoji {e.Emoji.Name}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to revoke role {role.Id} from member {member.Id}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in reaction removed handler");
            }
        }
    }
}
