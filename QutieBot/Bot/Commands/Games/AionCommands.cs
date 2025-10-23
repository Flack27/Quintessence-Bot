using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using QutieBot.Bot.GoogleSheets;
using QutieDAL.GamesDAL;
using QutieDTO.Models;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieBot.Bot.Commands.Games
{
    [Command("aion")]
    public class AionCommands
    {
        private readonly AionCommandsDAL _dal;
        private readonly GoogleSheetsFacade _sheets;
        private readonly ILogger<AionCommands> _logger;
        private const int GameId = 8; // ← SET THIS!

        public AionCommands(
            AionCommandsDAL dal,
            GoogleSheetsFacade sheets,
            ILogger<AionCommands> logger)
        {
            _dal = dal;
            _sheets = sheets;
            _logger = logger;
        }

        [Command("commands"), Description("List all available AION commands")]
        public async Task ExplainCommands(CommandContext ctx)
        {
            _logger.LogInformation($"User {ctx.User.Id} requested AION commands list");

            var commandInfo = new StringBuilder();

            commandInfo.AppendLine("**AION Commands:**");
            commandInfo.AppendLine();

            commandInfo.AppendLine("**Character Commands:**");
            commandInfo.AppendLine("`/aion profile` - View your character profile");
            commandInfo.AppendLine("`/aion char` - Update your character details");
            commandInfo.AppendLine();

            commandInfo.AppendLine("**Character Update Parameters:**");
            commandInfo.AppendLine("- `name` - Your in-game character name");
            commandInfo.AppendLine("- `gearscore` - Your current gearscore");
            commandInfo.AppendLine("- `class` - Your character class");
            commandInfo.AppendLine("- `role` - Your role (DPS, Tank, Healer, Support)");

            var embed = new DiscordEmbedBuilder()
                .WithTitle("AION Commands")
                .WithDescription(commandInfo.ToString())
                .WithColor(DiscordColor.Gold);

            await ctx.RespondAsync(embed.Build());
        }

        [Command("profile"), Description("View your AION character profile")]
        public async Task GetAionData(CommandContext ctx)
        {
            _logger.LogInformation($"User {ctx.User.Id} requested their AION profile");

            var gameData = await _dal.GetAionDataAsync(ctx.User.Id);
            if (gameData == null)
            {
                _logger.LogInformation($"No AION data found for user {ctx.User.Id}");
                await ctx.RespondAsync("You don't have a character profile yet. Use `/aion char` to create one!");
                return;
            }

            var rosterIds = await _dal.GetRoster(GameId);

            DiscordRole? roster = null;
            if (rosterIds != null)
            {
                roster = ctx.Member?.Roles.FirstOrDefault(role => rosterIds.Contains((long)role.Id));
            }

            var embed = new DiscordEmbedBuilder
            {
                Title = $"{ctx.User.GlobalName}'s AION Profile",
                Color = DiscordColor.Gold,
                Thumbnail = new DiscordEmbedBuilder.EmbedThumbnail { Url = ctx.User.AvatarUrl },
                Timestamp = System.DateTime.UtcNow
            };

            var description = new StringBuilder();
            description.AppendLine($"**Character:** {gameData.IGN ?? "Not set"}");
            description.AppendLine($"**Gearscore:** {gameData.Gearscore?.ToString() ?? "Not set"}");
            description.AppendLine($"**Roster:** {roster?.Name ?? "None"}");
            description.AppendLine();
            description.AppendLine($"**Class:** {gameData.Class ?? "Not set"}");
            description.AppendLine($"**Role:** {gameData.Role ?? "Not set"}");

            embed.WithDescription(description.ToString());

            await ctx.RespondAsync(embed);
        }

        [Command("char"), Description("Update your AION character details")]
        public async Task UpdateAionData(CommandContext ctx,
           [Description("Your character name")] string? name = null,
           [Description("Your gearscore")] int? gearscore = null,
           [Description("Your class"), SlashChoiceProvider<ClassProvider>] string? @class = null,
           [Description("Your role"), SlashChoiceProvider<RoleProvider>] string? role = null)
        {
            _logger.LogInformation($"User {ctx.User.Id} updating AION character data");

            var gameData = new AionData
            {
                UserId = (long)ctx.User.Id,
                GameId = GameId,
                IGN = name,
                Gearscore = gearscore,
                Class = @class,
                Role = role
            };

            await _dal.SaveOrUpdateAionDataAsync(gameData);

            var game = await _dal.GetGameRoles(GameId);

            if (game == null)
            {
                _logger.LogError($"Could not find game with ID {GameId}");
                await ctx.RespondAsync("Something went wrong. Please try again later.");
                return;
            }

            if (ctx.Member.Roles.Any(g => g.Id == (ulong)game.RoleId))
            {
                await _sheets.UpdateUserAsync((long)ctx.User.Id, game);
            }

            // Get roster info for the success message
            var rosterIds = await _dal.GetRoster(GameId);
            DiscordRole? roster = null;
            if (rosterIds != null)
            {
                roster = ctx.Member?.Roles.FirstOrDefault(r => rosterIds.Contains((long)r.Id));
            }

            var embed = new DiscordEmbedBuilder
            {
                Title = $"{ctx.User.GlobalName}'s AION Profile",
                Description = "✅ Character updated successfully!",
                Color = DiscordColor.Green,
                Thumbnail = new DiscordEmbedBuilder.EmbedThumbnail { Url = ctx.User.AvatarUrl },
                Timestamp = System.DateTime.UtcNow
            };

            var details = new StringBuilder();
            details.AppendLine($"**Character:** {name}");
            details.AppendLine($"**Gearscore:** {gearscore}");
            details.AppendLine($"**Roster:** {roster?.Name ?? "None"}");
            details.AppendLine();
            details.AppendLine($"**Class:** {@class}");
            details.AppendLine($"**Role:** {role}");

            embed.AddField("Profile Details", details.ToString());

            await ctx.RespondAsync(embed);
        }

        // Choice providers
        private class ClassProvider : IChoiceProvider
        {
            public ValueTask<IReadOnlyDictionary<string, object>> ProvideAsync(CommandParameter parameter)
            {
                var choices = new Dictionary<string, object>
                {
                    { "Gladiator", "Gladiator" },
                    { "Templar", "Templar" },
                    { "Assassin", "Assassin" },
                    { "Ranger", "Ranger" },
                    { "Sorcerer", "Sorcerer" },
                    { "Spiritmaster", "Spiritmaster" },
                    { "Cleric", "Cleric" },
                    { "Chanter", "Chanter" }
                };

                return new ValueTask<IReadOnlyDictionary<string, object>>(choices);
            }
        }

        private class RoleProvider : IChoiceProvider
        {
            public ValueTask<IReadOnlyDictionary<string, object>> ProvideAsync(CommandParameter parameter)
            {
                var choices = new Dictionary<string, object>
                {
                    { "DPS", "DPS" },
                    { "Tank", "Tank" },
                    { "Healer", "Healer" },
                    { "Support", "Support" }
                };

                return new ValueTask<IReadOnlyDictionary<string, object>>(choices);
            }
        }
    }
}