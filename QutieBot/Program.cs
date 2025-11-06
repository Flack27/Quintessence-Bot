using DSharpPlus.Entities;
using DSharpPlus;
using Microsoft.Extensions.DependencyInjection;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.TextCommands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.Interactivity;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus.Commands.Processors.TextCommands.Parsing;
using DSharpPlus.Extensions;
using QutieDAL.DAL;
using QutieBot.Bot;
using Microsoft.EntityFrameworkCore;
using QutieDTO.Models;
using Microsoft.Extensions.Hosting;
using QutieDAL.GamesDAL;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Sheets.v4;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;
using DSharpPlus.Exceptions;
using QutieBot.Bot.GoogleSheets;
using QutieBot.Bot.Commands;
using QutieBot.Bot.Commands.Games;
using Serilog;
using Serilog.Events;
using QutieBot.Bot.Services;
using Serilog.Extensions.Hosting;

namespace QutieBot.Bot.Commands
{
    /// <summary>
    /// Entry point for the QutieBot application
    /// </summary>
    class Program
    {
        /// <summary>
        /// Main application entry point
        /// </summary>
        static async Task Main(string[] args)
        {
            // Set up log path
            string logPath = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            Directory.CreateDirectory(logPath); // Ensure directory exists

            // Configure Serilog first to ensure logging is available early
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Infrastructure", LogEventLevel.Warning)
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(logPath, "qutiebot-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 10 * 1024 * 1024)
                .CreateLogger();

            try
            {
                // Create and start the host
                await CreateHostBuilder(args).Build().RunAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application failed to start");
            }
            finally
            {
                // Ensure we flush any pending log messages when the app shuts down
                Log.CloseAndFlush();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()  // Use Serilog for logging
                .ConfigureServices((hostContext, services) =>
                {
                    var configuration = hostContext.Configuration;

                    // Get configuration settings
                    string botToken = configuration["DiscordBot:Token"]
                        ?? throw new InvalidOperationException("BotToken is not configured.");

                    string connectionString = configuration["Database:ConnectionString"]
                        ?? throw new InvalidOperationException("ConnectionString is not configured.");

                    string apiKey = configuration["RaidHelper:ApiKey"]
                        ?? throw new InvalidOperationException("RaidHelper API key is not configured.");

                    // Configure Google Sheets credentials
                    var credentialPath = Path.Combine(Directory.GetCurrentDirectory(), "account_secret.json");
                    var credential = GoogleCredential.FromFile(credentialPath)
                        .CreateScoped(new[] { SheetsService.Scope.Spreadsheets });

                    // Register Google Sheets service
                    var sheetService = new SheetsService(new BaseClientService.Initializer()
                    {
                        HttpClientInitializer = credential,
                        ApplicationName = "QutieBot"
                    });
                    services.AddSingleton(sheetService);

                    // Database context
                    services.AddDbContextFactory<QutieDataTestContext>(options =>
                        options.UseSqlServer(connectionString));

                    // Register DAL services
                    RegisterDataAccessLayers(services);

                    // Register application services
                    RegisterApplicationServices(services);

                    // Register Discord client and extensions
                    services.AddDiscordClient(botToken, DiscordIntents.All | DiscordIntents.MessageContents | SlashCommandProcessor.RequiredIntents);
                    services.AddInteractivityExtension(new InteractivityConfiguration { Timeout = TimeSpan.FromMinutes(2) });
                    services.AddCommandsExtension(services =>
                    {
                        services.AddCommands(new[] { typeof(AdminCommands), typeof(AocCommands), typeof(UserCommands), typeof(WwmCommands), typeof(AionCommands) });
                        services.AddProcessors(new SlashCommandProcessor());
                    });

                    // Register event handlers
                    services.ConfigureEventHandlers(b => b.AddEventHandlers<EventHandlers>(ServiceLifetime.Singleton));

                    // HTTP client for RaidHelper
                    services.AddHttpClient("RaidHelper", client =>
                    {
                        client.DefaultRequestHeaders.Add("Authorization", apiKey);
                    });

                    // Add Discord bot startup service
                    services.AddHostedService<DiscordBotStartupService>();
                });

        /// <summary>
        /// Registers all data access layer services
        /// </summary>
        private static void RegisterDataAccessLayers(IServiceCollection services)
        {
            services.AddSingleton<DiscordInfoSaverDAL>();
            services.AddSingleton<JoinToCreateManagerDAL>();
            services.AddSingleton<RaidHelperManagerDAL>();
            services.AddSingleton<UserMessageXPCounterDAL>();
            services.AddSingleton<UserVoiceXPCounterDAL>();
            services.AddSingleton<ReactionRoleManagerDAL>();
            services.AddSingleton<ReactionRoleHandlerDAL>();
            services.AddSingleton<GenerateImageDAL>();
            services.AddSingleton<GoogleSheetsDAL>();
            services.AddSingleton<UserSheetDAL>();
            services.AddSingleton<CommandsDAL>();
            services.AddSingleton<AutomatedCheckDAL>();
            services.AddSingleton<AutoRoleDAL>();

            services.AddSingleton<AocCommandsDAL>();
            services.AddScoped<WwmCommandsDAL>();
            services.AddSingleton<AionCommandsDAL>();
        }

        /// <summary>
        /// Registers all application services
        /// </summary>
        private static void RegisterApplicationServices(IServiceCollection services)
        {
            // Google Sheets services
            services.AddSingleton<UserSheetService>();
            services.AddSingleton<EventSheetService>();
            services.AddSingleton<AttendanceSheetService>();
            services.AddSingleton<GoogleSheetsFacade>();

            // Bot services
            services.AddSingleton<InterviewRoom>();
            services.AddSingleton<CommandsModule>();
            services.AddSingleton<EventHandlers>();
            services.AddSingleton<DiscordInfoSaver>();
            services.AddSingleton<JoinToCreateManager>();
            services.AddSingleton<RaidHelperManager>();
            services.AddSingleton<UserMessageXPCounter>();
            services.AddSingleton<UserVoiceXPCounter>();
            services.AddSingleton<ReactionRoleManager>();
            services.AddSingleton<ReactionRoleHandler>();
            services.AddSingleton<GenerateImage>();
            services.AddSingleton<AutomatedCheckService>();
            services.AddSingleton<WelcomeLeaveMessenger>();
            services.AddSingleton<AutoRoleManager>();
            services.AddSingleton<DmRelayService>();
            services.AddSingleton<InterviewFollowUpService>();

            // Background services
            services.AddHostedService<ScheduleMemberCountUpdateService>();
            services.AddHostedService<ScheduleEventService>();

            // Command modules
            services.AddSingleton<UserCommands>();
            services.AddSingleton<AdminCommands>();
            services.AddSingleton<AocCommands>();
            services.AddSingleton<WwmCommands>();
            services.AddSingleton<AionCommands>();

            // Webhook service
            services.AddSingleton<Webhook>();
        }
    }
}