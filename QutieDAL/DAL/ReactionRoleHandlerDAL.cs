using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Logging;
using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieDAL.DAL
{
    public class ReactionRoleHandlerDAL
    {
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<CommandsDAL> _logger;

        public ReactionRoleHandlerDAL(
            IDbContextFactory<QutieDataTestContext> contextFactory,
            ILogger<CommandsDAL> logger)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        public async Task<ReactionRoles> GetReactionRole(long channelId, long messageId, string emojiName, long emojiId)
        {
            try
            {
                _logger.LogInformation($"Getting reaction role for emoji {emojiName} on message {messageId}");

                using var context = _contextFactory.CreateDbContext();

                var reactionRole = await context.ReactionRoles
                    .FirstOrDefaultAsync(r =>
                        r.ChannelId == channelId &&
                        r.MessageId == messageId &&
                        r.EmojiName == emojiName &&
                        r.EmojiId == emojiId);

                if (reactionRole != null)
                {
                    _logger.LogInformation($"Found reaction role (ID: {reactionRole.Id}) for emoji {emojiName}");
                }
                else
                {
                    _logger.LogInformation($"No reaction role found for emoji {emojiName} on message {messageId}");
                }

                return reactionRole;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving reaction role for message {messageId} in channel {channelId}");
                return null;
            }
        }
    }
}
