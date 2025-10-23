using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Commands.Trees.Metadata;
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
    [Command("wwm")]
    public class WwmCommands
    {
        private readonly WwmCommandsDAL _dal;
        private readonly GoogleSheetsFacade _sheets;
        private readonly ILogger<WwmCommands> _logger;
        private const int GameId = 7;

        public WwmCommands(
            WwmCommandsDAL dal,
            GoogleSheetsFacade sheets,
            ILogger<WwmCommands> logger)
        {
            _dal = dal;
            _sheets = sheets;
            _logger = logger;
        }

        [Command("commands"), Description("List all available WWM commands")]
        public async Task ExplainCommands(CommandContext ctx)
        {
            _logger.LogInformation($"User {ctx.User.Id} requested WWM commands list");

            var commandInfo = new StringBuilder();

            commandInfo.AppendLine("**Where Winds Meet Commands:**");
            commandInfo.AppendLine();

            commandInfo.AppendLine("**Character Commands:**");
            commandInfo.AppendLine("`/wwm profile` - View your character profile");
            commandInfo.AppendLine("`/wwm char` - Update your character details");
            commandInfo.AppendLine();

            commandInfo.AppendLine("**Character Update Parameters:**");
            commandInfo.AppendLine("- `name` - Your in-game character name");
            commandInfo.AppendLine("- `level` - Your current character level");
            commandInfo.AppendLine("- `primary` - Your primary weapon");
            commandInfo.AppendLine("- `secondary` - Your secondary weapon");
            commandInfo.AppendLine("- `role` - Your role (DPS, Support, Healer)");
            commandInfo.AppendLine("- `style` - Your gameplay style (Casual, Hardcore, etc.)");

            var embed = new DiscordEmbedBuilder()
                .WithTitle("Where Winds Meet Commands")
                .WithDescription(commandInfo.ToString())
                .WithColor(DiscordColor.Cyan);

            await ctx.RespondAsync(embed.Build());
        }

        [Command("profile"), Description("View your Where Winds Meet character profile")]
        public async Task GetWwmData(CommandContext ctx)
        {
            _logger.LogInformation($"User {ctx.User.Id} requested their WWM profile");

            var gameData = await _dal.GetWwmDataAsync(ctx.User.Id);
            if (gameData == null)
            {
                _logger.LogInformation($"No WWM data found for user {ctx.User.Id}");
                await ctx.RespondAsync("You don't have a character profile yet. Use `/wwm char` to create one!");
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
                Title = $"{ctx.User.GlobalName}'s Where Winds Meet Profile",
                Color = DiscordColor.Cyan,
                Thumbnail = new DiscordEmbedBuilder.EmbedThumbnail { Url = ctx.User.AvatarUrl },
                Timestamp = System.DateTime.UtcNow
            };

            var description = new StringBuilder();
            description.AppendLine($"**Character:** {gameData.IGN ?? "Not set"}");
            description.AppendLine($"**Level:** {gameData.Level?.ToString() ?? "Not set"}");
            description.AppendLine($"**Roster:** {roster?.Name ?? "None"}");
            description.AppendLine();
            description.AppendLine($"**Primary Weapon:** {gameData.PrimaryWeapon ?? "Not set"}");
            description.AppendLine($"**Secondary Weapon:** {gameData.SecondaryWeapon ?? "Not set"}");
            description.AppendLine($"**Role:** {gameData.Role ?? "Not set"}");
            description.AppendLine($"**Playstyle:** {gameData.Playstyle ?? "Not set"}");

            embed.WithDescription(description.ToString());

            await ctx.RespondAsync(embed);
        }

        [Command("char"), Description("Update your Wuthering Waves character details")]
        public async Task UpdateWwmData(CommandContext ctx,
                    [Description("Your character name")] string? name = null,
                    [Description("Your character level")] int? level = null,
                    [Description("Your primary weapon"), SlashChoiceProvider<PrimaryWeaponProvider>] string? primary = null,
                    [Description("Your secondary weapon"), SlashChoiceProvider<SecondaryWeaponProvider>] string? secondary = null,
                    [Description("Your preferred role"), SlashChoiceProvider<RoleProvider>] string? role = null,
                    [Description("Your gameplay style"), SlashChoiceProvider<PlaystyleProvider>] string? style = null)
        {
            _logger.LogInformation($"User {ctx.User.Id} updating WWM character data");

            var gameData = new WwmData
            {
                UserId = (long)ctx.User.Id,
                GameId = GameId,
                IGN = name,
                Level = level,
                PrimaryWeapon = primary,
                SecondaryWeapon = secondary,
                Role = role,
                Playstyle = style
            };

            await _dal.SaveOrUpdateWwmDataAsync(gameData);

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

            // Get the updated data to show in response
            var updatedData = await _dal.GetWwmDataAsync(ctx.User.Id);
            if (updatedData == null)
            {
                await ctx.RespondAsync("Failed to retrieve updated profile. Please try again.");
                return;
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
                Title = $"{ctx.User.GlobalName}'s Where Winds Meet Profile",
                Description = "✅ Character updated successfully!",
                Color = DiscordColor.Green,
                Thumbnail = new DiscordEmbedBuilder.EmbedThumbnail { Url = ctx.User.AvatarUrl },
                Timestamp = System.DateTime.UtcNow
            };

            var details = new StringBuilder();
            details.AppendLine($"**Character:** {updatedData.IGN ?? "Not set"}");
            details.AppendLine($"**Level:** {updatedData.Level?.ToString() ?? "Not set"}");
            details.AppendLine($"**Roster:** {roster?.Name ?? "None"}");
            details.AppendLine();
            details.AppendLine($"**Primary Weapon:** {updatedData.PrimaryWeapon ?? "Not set"}");
            details.AppendLine($"**Secondary Weapon:** {updatedData.SecondaryWeapon ?? "Not set"}");
            details.AppendLine($"**Role:** {updatedData.Role ?? "Not set"}");
            details.AppendLine($"**Playstyle:** {updatedData.Playstyle ?? "Not set"}");

            embed.AddField("Profile Details", details.ToString());

            await ctx.RespondAsync(embed);
        }

        // Choice providers for weapons
        private class PrimaryWeaponProvider : IChoiceProvider
        {
            public ValueTask<IReadOnlyDictionary<string, object>> ProvideAsync(CommandParameter parameter)
            {
                var choices = new Dictionary<string, object>
                {
                    { "Swords", "Swords" },
                    { "Dual Blades", "Dual Blades" },
                    { "Spears", "Spears" },
                    { "Rope Darts", "Rope Darts" },
                    { "Fans", "Fans" },
                    { "Combat Umbrellas", "Combat Umbrellas" },
                    { "Mo Blades", "Mo Blades" },
                    { "Bows", "Bows" }
                };

                return new ValueTask<IReadOnlyDictionary<string, object>>(choices);
            }
        }

        private class SecondaryWeaponProvider : IChoiceProvider
        {
            public ValueTask<IReadOnlyDictionary<string, object>> ProvideAsync(CommandParameter parameter)
            {
                var choices = new Dictionary<string, object>
                {
                    { "None", "None" },
                    { "Swords", "Swords" },
                    { "Dual Blades", "Dual Blades" },
                    { "Spears", "Spears" },
                    { "Rope Darts", "Rope Darts" },
                    { "Fans", "Fans" },
                    { "Combat Umbrellas", "Combat Umbrellas" },
                    { "Mo Blades", "Mo Blades" },
                    { "Bows", "Bows" }
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
                    { "Support", "Support" },
                    { "Tank", "Tank" },
                    { "Hybrid", "Hybrid" }
                };

                return new ValueTask<IReadOnlyDictionary<string, object>>(choices);
            }
        }

        private class PlaystyleProvider : IChoiceProvider
        {
            public ValueTask<IReadOnlyDictionary<string, object>> ProvideAsync(CommandParameter parameter)
            {
                var choices = new Dictionary<string, object>
                {
                    { "Casual", "Casual" },
                    { "Semi-HC", "Semi-HC" },
                    { "Hardcore", "Hardcore" },
                    { "No-Life", "No-Life" },
                };

                return new ValueTask<IReadOnlyDictionary<string, object>>(choices);
            }
        }
    }
}