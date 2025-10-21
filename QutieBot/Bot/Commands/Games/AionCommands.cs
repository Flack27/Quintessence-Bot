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
            commandInfo.AppendLine("- `role` - Your preferred role (DPS, Tank, Healer, Support)");

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
                Title = $"{ctx.User.GlobalName}'s Character Profile",
                Color = DiscordColor.Gold,
                Thumbnail = new DiscordEmbedBuilder.EmbedThumbnail { Url = ctx.User.AvatarUrl },
                Timestamp = System.DateTime.UtcNow
            };

            embed.AddField("Character Name", gameData.IGN ?? "Not set", true)
                 .AddField("Gearscore", gameData.Gearscore?.ToString() ?? "Not set", true)
                 .AddField("Roster", roster?.Name ?? "None", true)
                 .AddField("Class", gameData.Class ?? "Not set", true)
                 .AddField("Role", gameData.Role ?? "Not set", true);

            await ctx.RespondAsync(embed);
        }

        [Command("char"), Description("Update your AION character details")]
        public async Task UpdateAionData(CommandContext ctx,
            [Description("Your character name")] string name,
            [Description("Your gearscore")] int gearscore,
            [Description("Your character class"), SlashChoiceProvider<ClassProvider>] string @class,
            [Description("Your preferred role"), SlashChoiceProvider<RoleProvider>] string role)
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

            var messageBuilder = new StringBuilder();
            messageBuilder.AppendLine("✅ Character updated successfully!");
            messageBuilder.AppendLine($"• Name: {name}");
            messageBuilder.AppendLine($"• Gearscore: {gearscore}");
            messageBuilder.AppendLine($"• Class: {@class}");
            messageBuilder.AppendLine($"• Role: {role}");
            messageBuilder.AppendLine("\nUse `/aion profile` to view your complete profile.");

            await ctx.RespondAsync(messageBuilder.ToString());
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