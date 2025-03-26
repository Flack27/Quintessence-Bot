using DSharpPlus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QutieBot.Bot;
using QutieBot.Bot.Services;

namespace QutieBot
{
    /// <summary>
    /// Base class for implementing a scheduled background service
    /// </summary>
    public abstract class ScheduledBackgroundService : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly TimeSpan _interval;
        private readonly string _serviceName;

        protected ScheduledBackgroundService(
            TimeSpan interval,
            string serviceName,
            ILogger logger)
        {
            _interval = interval;
            _serviceName = serviceName;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"{_serviceName} background service is starting");

            // Optional: initial delay to stagger services
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            // Run the service until the application is stopped
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation($"Executing {_serviceName} service task");
                    await ExecuteScheduledTaskAsync(stoppingToken);
                    _logger.LogInformation($"Completed {_serviceName} service task");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error executing {_serviceName} service task");
                }

                try
                {
                    await Task.Delay(_interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation($"{_serviceName} background service is stopping");
        }

        /// <summary>
        /// The task to execute on the schedule
        /// </summary>
        protected abstract Task ExecuteScheduledTaskAsync(CancellationToken stoppingToken);
    }

    /// <summary>
    /// Background service for updating events from RaidHelper
    /// </summary>
    public class ScheduleEventService : ScheduledBackgroundService
    {
        private readonly RaidHelperManager _raidHelper;
        private readonly AutomatedCheckService _automatedCheck;
        private readonly ILogger<ScheduleEventService> _logger;

        public ScheduleEventService(
            RaidHelperManager raidHelper,
            AutomatedCheckService automatedCheck,
            ILogger<ScheduleEventService> logger)
            : base(TimeSpan.FromHours(1), "Event Update", logger)
        {
            _raidHelper = raidHelper;
            _automatedCheck = automatedCheck;
            _logger = logger;
        }

        protected override async Task ExecuteScheduledTaskAsync(CancellationToken stoppingToken)
        {
            try
            {
                // First ensure RaidHelper data is up-to-date
                _logger.LogInformation("Running RaidHelper update before automated checks");
                await _raidHelper.UpdateEvents();

                // Then run the automated checks
                _logger.LogInformation("RaidHelper update completed, running automated checks");
                await _automatedCheck.MonitorEvents(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during coordinated automated checks execution");
            }
        }
    }

    /// <summary>
    /// Background service for updating member counts
    /// </summary>
    public class ScheduleMemberCountUpdateService : ScheduledBackgroundService
    {
        private readonly DiscordInfoSaver _discordInfoSaver;
        private readonly DiscordClient _client;

        public ScheduleMemberCountUpdateService(
            DiscordInfoSaver discordInfoSaver,
            DiscordClient client,
            ILogger<ScheduleMemberCountUpdateService> logger)
            : base(TimeSpan.FromHours(1), "Member Count Update", logger)
        {
            _discordInfoSaver = discordInfoSaver;
            _client = client;
        }

        protected override async Task ExecuteScheduledTaskAsync(CancellationToken stoppingToken)
        {
            foreach (var guild in _client.Guilds.Values)
            {
                await _discordInfoSaver.UpdateMemberCountChannelName(guild);
            }
        }
    }
}