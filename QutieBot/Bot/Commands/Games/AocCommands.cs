using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Commands.Trees;
using DSharpPlus.Commands.Trees.Metadata;
using DSharpPlus.Entities;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Logging;
using QutieBot.Bot.GoogleSheets;
using QutieDAL.GamesDAL;
using QutieDTO.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace QutieBot.Bot.Commands.Games
{


    [Command("aoc")]
    public class AocCommands
    {
        private readonly AocCommandsDAL _dal;
        private readonly GoogleSheetsFacade _sheets;
        private readonly ILogger<AocCommands> _logger;
        private const int GameId = 4;

        public AocCommands(
            AocCommandsDAL dal,
            GoogleSheetsFacade sheets,
            ILogger<AocCommands> logger)
        {
            _dal = dal;
            _sheets = sheets;
            _logger = logger;
        }

        [Command("commands"), Description("List all available Ashes of Creation commands")]
        public async Task ExplainCommands(CommandContext ctx)
        {
            _logger.LogInformation($"User {ctx.User.Id} requested AoC commands list");

            var commandInfo = new StringBuilder();

            commandInfo.AppendLine("**Ashes of Creation Commands:**");
            commandInfo.AppendLine();

            // Profile commands
            commandInfo.AppendLine("**Character Commands:**");
            commandInfo.AppendLine("`/aoc profile` - View your character profile");
            commandInfo.AppendLine("`/aoc char` - Update your character details (name, level, class, etc.)");
            commandInfo.AppendLine("`/aoc craft` - Update your crafting professions");
            commandInfo.AppendLine();

            // Parameter details
            commandInfo.AppendLine("**Character Update Parameters:**");
            commandInfo.AppendLine("- `name` - Your in-game character name");
            commandInfo.AppendLine("- `level` - Your current character level");
            commandInfo.AppendLine("- `class` - Your primary archetype");
            commandInfo.AppendLine("- `role` - Your combat role (Tank, DPS, etc.)");
            commandInfo.AppendLine("- `style` - Your gameplay style (Casual, Hardcore, etc.)");
            commandInfo.AppendLine();

            commandInfo.AppendLine("**Crafting Update Parameters:**");
            commandInfo.AppendLine("- `primary` - Your primary crafting/processing profession");
            commandInfo.AppendLine("- `primary_tier` - Your primary profession skill level");
            commandInfo.AppendLine("- `secondary` - Your secondary gathering profession");
            commandInfo.AppendLine("- `secondary_tier` - Your secondary profession skill level");
            commandInfo.AppendLine("- `tertiary` - Your tertiary gathering profession");
            commandInfo.AppendLine("- `tertiary_tier` - Your tertiary profession skill level");
            commandInfo.AppendLine();

            commandInfo.AppendLine("_Note: Leave any parameter empty to keep its current value_");

            var embed = new DiscordEmbedBuilder()
                .WithTitle("Ashes of Creation Commands")
                .WithDescription(commandInfo.ToString())
                .WithColor(DiscordColor.HotPink);

            await ctx.RespondAsync(embed.Build());
        }

        [Command("profile"), Description("View your Ashes of Creation character profile")]
        public async Task GetAocData(CommandContext ctx)
        {
            _logger.LogInformation($"User {ctx.User.Id} requested their AoC profile");

            var gameData = await _dal.GetAoCDataAsync(ctx.User.Id);
            if (gameData == null)
            {
                _logger.LogInformation($"No AoC data found for user {ctx.User.Id}");
                await ctx.RespondAsync("You don't have a character profile yet. Use `/aoc char` to create one!");
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
                Color = DiscordColor.HotPink,
                Thumbnail = new DiscordEmbedBuilder.EmbedThumbnail { Url = ctx.User.AvatarUrl },
                Timestamp = DateTime.UtcNow
            };

            embed.AddField("Character Name", gameData.IGN ?? "Not set", true)
                 .AddField("Level", gameData.Level?.ToString() ?? "Not set", true)
                 .AddField("Roster", roster?.Name ?? "None", true)
                 .AddField("Class", gameData.Class ?? "Not set", true)
                 .AddField("Role", gameData.Role ?? "Not set", true)
                 .AddField("Playstyle", gameData.Playstyle ?? "Not set", true);

            // Only show professions section if any professions are set
            if (!string.IsNullOrEmpty(gameData.PrimaryProfession) ||
                !string.IsNullOrEmpty(gameData.SecondaryProfession) ||
                !string.IsNullOrEmpty(gameData.TertiaryProfession))
            {
                embed.AddField("Professions",
                    $"**Primary:** {gameData.PrimaryProfession ?? "None"} {(string.IsNullOrEmpty(gameData.PrimaryTier) ? "" : $"({gameData.PrimaryTier})")}\n" +
                    $"**Secondary:** {gameData.SecondaryProfession ?? "None"} {(string.IsNullOrEmpty(gameData.SecondaryTier) ? "" : $"({gameData.SecondaryTier})")}\n" +
                    $"**Tertiary:** {gameData.TertiaryProfession ?? "None"} {(string.IsNullOrEmpty(gameData.TertiaryTier) ? "" : $"({gameData.TertiaryTier})")}",
                    false);
            }

            await ctx.RespondAsync(embed);
        }

        [Command("char"), Description("Update your Ashes of Creation character details")]
        public async Task UpdateAocData(CommandContext ctx,
            [Description("Your character name")] string name,
            [Description("Your character level")] int level,
            [Description("Your character class"), SlashChoiceProvider<ClassProvider>] string @class,
            [Description("Your combat role"), SlashChoiceProvider<RoleProvider>] string role,
            [Description("Your gameplay style"), SlashChoiceProvider<PlaystyleProvider>] string style)
        {
            _logger.LogInformation($"User {ctx.User.Id} updating AoC character data");

            var gameData = new AocData
            {
                UserId = (long)ctx.User.Id,
                GameId = GameId,
                IGN = name,
                Level = level,
                Class = @class,
                Role = role,
                Playstyle = style
            };

            // Use the game data service 
            await _dal.SaveOrUpdateAoCDataAsync(gameData);

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

            // More descriptive success message
            var messageBuilder = new StringBuilder();
            messageBuilder.AppendLine("✅ Character updated successfully!");

            if (name != null) messageBuilder.AppendLine($"• Name: {name}");
            if (level != null) messageBuilder.AppendLine($"• Level: {level}");
            if (@class != null) messageBuilder.AppendLine($"• Class: {@class}");
            if (role != null) messageBuilder.AppendLine($"• Role: {role}");
            if (style != null) messageBuilder.AppendLine($"• Playstyle: {style}");

            messageBuilder.AppendLine("\nUse `/aoc profile` to view your complete profile.");

            await ctx.RespondAsync(messageBuilder.ToString());
        }

        [Command("craft"), Description("Update your Ashes of Creation crafting professions")]
        public async Task UpdateAocLifeskills(CommandContext ctx,
            [Description("Primary crafting profession"), SlashChoiceProvider<PrimaryProfessionProvider>] string? primary = null,
            [Description("Primary profession tier"), SlashChoiceProvider<ProfessionTierProvider>] string? primary_tier = null,
            [Description("Secondary gathering profession"), SlashChoiceProvider<SecondaryProfessionProvider>] string? secondary = null,
            [Description("Secondary profession tier"), SlashChoiceProvider<ProfessionTierProvider>] string? secondary_tier = null,
            [Description("Tertiary gathering profession"), SlashChoiceProvider<SecondaryProfessionProvider>] string? tertiary = null,
            [Description("Tertiary profession tier"), SlashChoiceProvider<ThirdProfessionTierProvider>] string? tertiary_tier = null)
        {
            _logger.LogInformation($"User {ctx.User.Id} updating AoC crafting data");

            var gameData = new AocData
            {
                UserId = (long)ctx.User.Id,
                GameId = GameId,
                PrimaryProfession = primary,
                PrimaryTier = primary_tier,
                SecondaryProfession = secondary,
                SecondaryTier = secondary_tier,
                TertiaryProfession = tertiary,
                TertiaryTier = tertiary_tier
            };

            // Use the game data service
            await _dal.SaveOrUpdateAoCDataAsync(gameData);

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

            // More descriptive success message
            var messageBuilder = new StringBuilder();
            messageBuilder.AppendLine("✅ Crafting professions updated successfully!");

            if (primary != null) messageBuilder.AppendLine($"• Primary: {primary} {(primary_tier != null ? $"({primary_tier})" : "")}");
            if (secondary != null) messageBuilder.AppendLine($"• Secondary: {secondary} {(secondary_tier != null ? $"({secondary_tier})" : "")}");
            if (tertiary != null) messageBuilder.AppendLine($"• Tertiary: {tertiary} {(tertiary_tier != null ? $"({tertiary_tier})" : "")}");

            messageBuilder.AppendLine("\nUse `/aoc profile` to view your complete profile.");

            await ctx.RespondAsync(messageBuilder.ToString());
        }


        private class ClassProvider : IChoiceProvider
        {
            public ValueTask<IReadOnlyDictionary<string, object>> ProvideAsync(CommandParameter parameter)
            {
                var choices = new Dictionary<string, object>
            {
                { "Mage", "Mage" },
                { "Ranger", "Ranger" },
                { "Bard", "Bard" },
                { "Rogue", "Rogue" },
                { "Cleric", "Cleric" },
                { "Tank", "Tank" },
                { "Summoner", "Summoner" },
                { "Fighter", "Fighter" },
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
                    { "Support", "Support" },
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

        private class PrimaryProfessionProvider : IChoiceProvider
        {
            public ValueTask<IReadOnlyDictionary<string, object>> ProvideAsync(CommandParameter parameter)
            {
                var choices = new Dictionary<string, object>
            {
                { "Alchemy", "Alchemy" },
                { "Animal Husbandry", "Animal Husbandry" },
                { "Cooking", "Cooking" },
                { "Farming", "Farming" },
                { "Lumber Milling", "Lumber Milling" },
                { "Metalworking", "Metalworking" },
                { "Stonemasonry", "Stonemasonry" },
                { "Tanning", "Tanning" },
                { "Weaving", "Weaving" },

                { "Arcane Engineering", "Arcane Engineering" },
                { "Armor Smithing", "Armor Smithing" },
                { "Carpentry", "Carpentry" },
                { "Jeweler", "Jeweler" },
                { "Leatherworking", "Leatherworking" },
                { "Scribe", "Scribe" },
                { "Tailoring", "Tailoring" },
                { "Weapon Smithing", "Weapon Smithing" }
            };

                return new ValueTask<IReadOnlyDictionary<string, object>>(choices);
            }
        }

        private class SecondaryProfessionProvider : IChoiceProvider
        {
            public ValueTask<IReadOnlyDictionary<string, object>> ProvideAsync(CommandParameter parameter)
            {
                var choices = new Dictionary<string, object>
            {
                { "Fishing", "Fishing" },
                { "Herbalism", "Herbalism" },
                { "Hunting", "Hunting" },
                { "Lumberjacking", "Lumberjacking" },
                { "Mining", "Mining" },
            };

                return new ValueTask<IReadOnlyDictionary<string, object>>(choices);
            }
        }

        private class ProfessionTierProvider : IChoiceProvider
        {
            public ValueTask<IReadOnlyDictionary<string, object>> ProvideAsync(CommandParameter parameter)
            {
                var choices = new Dictionary<string, object>
            {
                { "Apprentice", "Apprentice" },
                { "Journeyman", "Journeyman" },
                { "Master", "Master" },
                { "Grandmaster", "Grandmaster" },
            };

                return new ValueTask<IReadOnlyDictionary<string, object>>(choices);
            }
        }

        private class ThirdProfessionTierProvider : IChoiceProvider
        {
            public ValueTask<IReadOnlyDictionary<string, object>> ProvideAsync(CommandParameter parameter)
            {
                var choices = new Dictionary<string, object>
            {
                { "Apprentice", "Apprentice" },
                { "Journeyman", "Journeyman" },
                { "Master", "Master" }
            };

                return new ValueTask<IReadOnlyDictionary<string, object>>(choices);
            }
        }
    }
}
