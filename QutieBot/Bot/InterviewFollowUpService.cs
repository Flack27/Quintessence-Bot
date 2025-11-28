using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using QutieDTO.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QutieBot.Bot
{
    /// <summary>
    /// Manages automated follow-ups for interview rooms when applicants don't respond
    /// </summary>
    public class InterviewFollowUpService
    {
        private readonly ILogger<InterviewFollowUpService> _logger;
        private readonly StateManager _stateManager;
        private readonly ulong _adminRoleId;
        private readonly ConcurrentDictionary<ulong, InterviewTimer> _activeTimers;
        private readonly Dictionary<ulong, CancellationTokenSource> _cancellationTokens;
        private readonly SemaphoreSlim _timerLock = new SemaphoreSlim(1, 1);
        private InterviewRoom _interviewRoom;

        private const int FIRST_WARNING_HOURS = 24;
        private const int FINAL_WARNING_HOURS = 24;

        /// <summary>
        /// Initializes a new instance of the InterviewFollowUpService
        /// </summary>
        public InterviewFollowUpService(ILogger<InterviewFollowUpService> logger, StateManager stateManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _adminRoleId = 1152617541190041600;
            _activeTimers = new ConcurrentDictionary<ulong, InterviewTimer>();
            _cancellationTokens = new Dictionary<ulong, CancellationTokenSource>();
            _stateManager = stateManager;
        }

        /// <summary>
        /// Sets the InterviewRoom reference (called after both services are initialized)
        /// </summary>
        public void SetInterviewRoom(InterviewRoom interviewRoom)
        {
            _interviewRoom = interviewRoom ?? throw new ArgumentNullException(nameof(interviewRoom));
        }


        public async Task RestoreTimersAsync(DiscordClient client)
        {
            var savedTimers = _stateManager.GetInterviewTimers();
            foreach (var (channelId, timerState) in savedTimers)
            {
                try
                {
                    var channel = await client.GetChannelAsync(channelId);
                    if (channel == null) continue;

                    var timer = new InterviewTimer
                    {
                        ChannelId = timerState.ChannelId,
                        UserId = timerState.UserId,
                        AdminId = timerState.AdminId,
                        StartTime = timerState.StartTime,
                        FirstWarningSentAt = timerState.FirstWarningSentAt,
                        Stage = timerState.Stage,
                        IsPaused = timerState.IsPaused
                    };

                    _activeTimers[channelId] = timer;

                    if (!timer.IsPaused)
                    {
                        // Restart the timer task
                        var cts = new CancellationTokenSource();
                        _cancellationTokens[channelId] = cts;
                        _ = Task.Run(() => RunTimerAsync(client, channel, timer, cts.Token));
                    }

                    _logger.LogInformation($"Restored interview timer for channel {channelId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to restore timer for channel {channelId}");
                }
            }
        }


        /// <summary>
        /// Handles message creation in interview channels
        /// </summary>
        public async Task OnMessageCreated(DiscordClient client, MessageCreatedEventArgs e)
        {
            try
            {
                // Ignore bot messages
                if (e.Author.IsBot)
                    return;

                var channel = e.Channel;
                var channelId = channel.Id;

                // Check if this is an interview channel (by name pattern)
                if (!channel.Name.EndsWith("-interview"))
                    return;

                _logger.LogDebug($"Message in interview channel {channelId} from {e.Author.Username} ({e.Author.Id})");

                // Get the member to check their roles
                var member = await channel.Guild.GetMemberAsync(e.Author.Id);
                bool isAdmin = member.Roles.Any(r => r.Id == _adminRoleId);

                if (isAdmin)
                {
                    // Admin sent a message - start/restart timer (only if not paused)
                    if (_activeTimers.TryGetValue(channelId, out var existingTimer) && existingTimer.IsPaused)
                    {
                        _logger.LogInformation($"Timer for channel {channelId} is paused, not restarting");
                        return;
                    }
                    await StartOrRestartTimerAsync(client, channel, member.Id);
                }
                else
                {
                    // User sent a message - cancel timer
                    await CancelTimerAsync(channelId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling message in interview channel {e.Channel.Id}");
            }
        }

        /// <summary>
        /// Handles button interactions for timer control
        /// </summary>
        public async Task HandleTimerButtonAsync(DiscordClient client, ComponentInteractionCreatedEventArgs e)
        {
            try
            {
                if (!e.Id.StartsWith("timer_"))
                    return;

                var channel = e.Channel;
                var channelId = channel.Id;
                var guild = channel.Guild;
                var buttonUser = await guild.GetMemberAsync(e.User.Id);

                // Check if user has permission (admin role)
                if (!buttonUser.Roles.Any(r => r.Id == _adminRoleId))
                {
                    await e.Interaction.CreateResponseAsync(
                        DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder()
                            .WithContent("❌ You don't have permission to manage timers. Only admins can do this.")
                            .AsEphemeral(true));
                    return;
                }

                if (e.Id == $"timer_pause_{channelId}")
                {
                    await HandlePauseTimerAsync(e, channelId);
                }
                else if (e.Id == $"timer_extend_{channelId}")
                {
                    await HandleExtendTimerAsync(e, channelId);
                }
                else if (e.Id == $"timer_resume_{channelId}")
                {
                    await HandleResumeTimerAsync(e, channelId, client);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling timer button interaction");
            }
        }

        /// <summary>
        /// Handles pausing the timer
        /// </summary>
        private async Task HandlePauseTimerAsync(ComponentInteractionCreatedEventArgs e, ulong channelId)
        {
            await _timerLock.WaitAsync();
            try
            {
                if (_activeTimers.TryGetValue(channelId, out var timer))
                {
                    timer.IsPaused = true;

                    _stateManager.UpdateInterviewTimer(channelId, new InterviewTimerState
                    {
                        ChannelId = timer.ChannelId,
                        UserId = timer.UserId,
                        AdminId = timer.AdminId,
                        StartTime = timer.StartTime,
                        FirstWarningSentAt = timer.FirstWarningSentAt,
                        Stage = timer.Stage,
                        IsPaused = timer.IsPaused
                    });

                    // Cancel the running timer
                    if (_cancellationTokens.ContainsKey(channelId))
                    {
                        _cancellationTokens[channelId].Cancel();
                        _cancellationTokens[channelId].Dispose();
                        _cancellationTokens.Remove(channelId);
                    }

                    _logger.LogInformation($"Timer paused for channel {channelId} by admin {e.User.Id}");

                    await e.Interaction.CreateResponseAsync(
                        DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder()
                            .WithContent("⏸️ **Timer Paused** - The follow-up timer has been paused. It will not resume until you click the Resume button.")
                            .AsEphemeral(false));
                }
                else
                {
                    await e.Interaction.CreateResponseAsync(
                        DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder()
                            .WithContent("❌ No active timer found for this channel.")
                            .AsEphemeral(true));
                }
            }
            finally
            {
                _timerLock.Release();
            }
        }

        /// <summary>
        /// Handles extending the timer by 24 hours
        /// </summary>
        private async Task HandleExtendTimerAsync(ComponentInteractionCreatedEventArgs e, ulong channelId)
        {
            await _timerLock.WaitAsync();
            try
            {
                if (_activeTimers.TryGetValue(channelId, out var timer))
                {
                    // Add 24 hours to the timer by updating the start time
                    timer.StartTime = timer.StartTime.AddHours(24);

                    _stateManager.UpdateInterviewTimer(channelId, new InterviewTimerState
                    {
                        ChannelId = timer.ChannelId,
                        UserId = timer.UserId,
                        AdminId = timer.AdminId,
                        StartTime = timer.StartTime,
                        FirstWarningSentAt = timer.FirstWarningSentAt,
                        Stage = timer.Stage,
                        IsPaused = timer.IsPaused
                    });

                    _logger.LogInformation($"Timer extended by 24 hours for channel {channelId} by admin {e.User.Id}");

                    await e.Interaction.CreateResponseAsync(
                        DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder()
                            .WithContent("⏱️ **Timer Extended** - The follow-up timer has been extended by 24 hours. The applicant now has more time to respond.")
                            .AsEphemeral(false));
                }
                else
                {
                    await e.Interaction.CreateResponseAsync(
                        DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder()
                            .WithContent("❌ No active timer found for this channel.")
                            .AsEphemeral(true));
                }
            }
            finally
            {
                _timerLock.Release();
            }
        }

        /// <summary>
        /// Handles resuming a paused timer
        /// </summary>
        private async Task HandleResumeTimerAsync(ComponentInteractionCreatedEventArgs e, ulong channelId, DiscordClient client)
        {
            await _timerLock.WaitAsync();
            try
            {
                if (_activeTimers.TryGetValue(channelId, out var timer) && timer.IsPaused)
                {
                    timer.IsPaused = false;

                    _stateManager.UpdateInterviewTimer(channelId, new InterviewTimerState
                    {
                        ChannelId = timer.ChannelId,
                        UserId = timer.UserId,
                        AdminId = timer.AdminId,
                        StartTime = timer.StartTime,
                        FirstWarningSentAt = timer.FirstWarningSentAt,
                        Stage = timer.Stage,
                        IsPaused = timer.IsPaused
                    });

                    _logger.LogInformation($"Timer resumed for channel {channelId} by admin {e.User.Id}");

                    await e.Interaction.CreateResponseAsync(
                        DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder()
                            .WithContent("▶️ **Timer Resumed** - The follow-up timer is now active again. It will restart when you send your next admin message.")
                            .AsEphemeral(false));
                }
                else
                {
                    await e.Interaction.CreateResponseAsync(
                        DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder()
                            .WithContent("❌ No paused timer found for this channel.")
                            .AsEphemeral(true));
                }
            }
            finally
            {
                _timerLock.Release();
            }
        }

        /// <summary>
        /// Starts or restarts the timer for an interview channel after an admin message
        /// </summary>
        private async Task StartOrRestartTimerAsync(DiscordClient client, DiscordChannel channel, ulong adminId)
        {
            var channelId = channel.Id;

            await _timerLock.WaitAsync();
            try
            {
                // Cancel existing timer if any
                if (_cancellationTokens.ContainsKey(channelId))
                {
                    _cancellationTokens[channelId].Cancel();
                    _cancellationTokens[channelId].Dispose();
                    _cancellationTokens.Remove(channelId);
                }

                // Get user ID from InterviewRoom's active interviews
                ulong userId = GetUserIdFromInterviewRoom(channelId);

                if (userId == 0)
                {
                    _logger.LogWarning($"Could not find user ID for interview channel {channelId}");
                    return;
                }

                // Create new timer
                var timer = new InterviewTimer
                {
                    ChannelId = channelId,
                    UserId = userId,
                    AdminId = adminId,
                    StartTime = DateTimeOffset.UtcNow,
                    Stage = TimerStage.FirstWarning,
                    IsPaused = false
                };

                _activeTimers[channelId] = timer;

                _stateManager.UpdateInterviewTimer(channelId, new InterviewTimerState
                {
                    ChannelId = timer.ChannelId,
                    UserId = timer.UserId,
                    AdminId = timer.AdminId,
                    StartTime = timer.StartTime,
                    FirstWarningSentAt = timer.FirstWarningSentAt,
                    Stage = timer.Stage,
                    IsPaused = timer.IsPaused
                });

                // Create cancellation token
                var cts = new CancellationTokenSource();
                _cancellationTokens[channelId] = cts;

                _logger.LogInformation($"Started follow-up timer for interview channel {channelId} (User: {userId})");

                // Start the timer task (silently - no notification)
                _ = Task.Run(async () => await RunTimerAsync(client, channel, timer, cts.Token), cts.Token);
            }
            finally
            {
                _timerLock.Release();
            }
        }

        /// <summary>
        /// Gets the user ID from the InterviewRoom's active interviews dictionary
        /// </summary>
        private ulong GetUserIdFromInterviewRoom(ulong channelId)
        {
            if (_interviewRoom == null)
            {
                _logger.LogError("InterviewRoom reference not set in InterviewFollowUpService");
                return 0;
            }

            var activeInterviews = _interviewRoom.GetActiveInterviews();
            var interview = activeInterviews.Values.FirstOrDefault(i => i.ChannelId == channelId);
            
            return interview?.UserId ?? 0;
        }

        /// <summary>
        /// Cancels the timer for an interview channel when user responds
        /// </summary>
        private async Task CancelTimerAsync(ulong channelId)
        {
            await _timerLock.WaitAsync();
            try
            {
                if (_cancellationTokens.ContainsKey(channelId))
                {
                    _logger.LogInformation($"Cancelling follow-up timer for interview channel {channelId} (user responded)");
                    
                    _cancellationTokens[channelId].Cancel();
                    _cancellationTokens[channelId].Dispose();
                    _cancellationTokens.Remove(channelId);
                    _activeTimers.TryRemove(channelId, out _);
                    _stateManager.RemoveInterviewTimer(channelId);
                }
            }
            finally
            {
                _timerLock.Release();
            }
        }

        /// <summary>
        /// Runs the timer logic for an interview channel
        /// </summary>
        private async Task RunTimerAsync(DiscordClient client, DiscordChannel channel, InterviewTimer timer, CancellationToken cancellationToken)
        {
            try
            {
                // Wait for first warning period (24 hours from start time)
                var timeUntilWarning = timer.StartTime.AddHours(FIRST_WARNING_HOURS) - DateTimeOffset.UtcNow;
                
                if (timeUntilWarning > TimeSpan.Zero)
                {
                    _logger.LogInformation($"Waiting {timeUntilWarning.TotalHours:F1} hours before first warning for channel {channel.Id}");
                    await Task.Delay(timeUntilWarning, cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation($"Timer cancelled for channel {channel.Id} before first warning");
                    return;
                }

                // Send first warning
                await SendFirstWarningAsync(channel, timer.UserId);

                // Update stage
                timer.Stage = TimerStage.FinalWarning;
                timer.FirstWarningSentAt = DateTimeOffset.UtcNow;

                // Wait for final warning period (another 24 hours)
                _logger.LogInformation($"Waiting {FINAL_WARNING_HOURS} hours before closing channel {channel.Id}");
                await Task.Delay(TimeSpan.FromHours(FINAL_WARNING_HOURS), cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation($"Timer cancelled for channel {channel.Id} before closing");
                    return;
                }

                // Close the channel
                await CloseInterviewChannelAsync(channel, timer.UserId);

                // Clean up
                await _timerLock.WaitAsync();
                try
                {
                    _activeTimers.TryRemove(channel.Id, out _);
                    if (_cancellationTokens.ContainsKey(channel.Id))
                    {
                        _cancellationTokens[channel.Id].Dispose();
                        _cancellationTokens.Remove(channel.Id);
                    }
                }
                finally
                {
                    _timerLock.Release();
                }
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation($"Timer task for channel {channel.Id} was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in timer task for channel {channel.Id}");
            }
        }

        /// <summary>
        /// Sends the first warning message to the applicant
        /// </summary>
        private async Task SendFirstWarningAsync(DiscordChannel channel, ulong userId)
        {
            try
            {
                _logger.LogInformation($"Sending first warning to user {userId} in channel {channel.Id}");

                var embed = new DiscordEmbedBuilder()
                    .WithTitle("⚠️ Application Follow-Up")
                    .WithDescription($"<@{userId}> Hope you are doing well! We have not yet received a response from you regarding your application.\n\n" +
                                   "Your application will be denied and this ticket will be closed if we don't hear from you in the next **24 hours**.\n\n" +
                                   "Please let us know your availability for an interview or if you have any questions.")
                    .WithColor(DiscordColor.Orange)
                    .WithFooter("Quintessence Application System")
                    .WithTimestamp(DateTimeOffset.UtcNow);

                var messageBuilder = new DiscordMessageBuilder()
                    .WithContent($"<@{userId}>")
                    .AddEmbed(embed)
                    .WithAllowedMentions(Mentions.All);

                await channel.SendMessageAsync(messageBuilder);

                _logger.LogInformation($"First warning sent to user {userId} in channel {channel.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending first warning in channel {channel.Id}");
            }
        }

        /// <summary>
        /// Closes the interview channel due to no response
        /// </summary>
        private async Task CloseInterviewChannelAsync(DiscordChannel channel, ulong userId)
        {
            try
            {
                _logger.LogInformation($"Closing interview channel {channel.Id} for user {userId} due to no response");

                var closureEmbed = new DiscordEmbedBuilder()
                    .WithTitle("❌ Application Denied - No Response")
                    .WithDescription($"This application has been automatically denied due to lack of response from <@{userId}>.\n\n" +
                                   "The applicant did not respond within the required 48-hour timeframe.\n\n" +
                                   "This channel will be deleted in 10 seconds.")
                    .WithColor(DiscordColor.Red)
                    .WithFooter("Quintessence Application System")
                    .WithTimestamp(DateTimeOffset.UtcNow);

                var messageBuilder = new DiscordMessageBuilder()
                    .WithContent($"<@{userId}>")
                    .AddEmbed(closureEmbed)
                    .WithAllowedMentions(Mentions.All);

                await channel.SendMessageAsync(messageBuilder);

                // Wait 10 seconds before deleting
                await Task.Delay(TimeSpan.FromSeconds(10));

                // Update database and remove from active interviews before deletion
                if (_interviewRoom != null)
                {
                    await _interviewRoom.CleanupInterviewOnTimeout(userId, channel.Id);
                }

                // Delete the channel
                await channel.DeleteAsync("Interview closed automatically - no response from applicant");

                _logger.LogInformation($"Interview channel {channel.Id} deleted due to no response");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error closing interview channel {channel.Id}");
            }
        }

        /// <summary>
        /// Gets active timer information for a channel (for debugging/status)
        /// </summary>
        public InterviewTimer GetTimerInfo(ulong channelId)
        {
            return _activeTimers.TryGetValue(channelId, out var timer) ? timer : null;
        }

        /// <summary>
        /// Manually cancels a timer (e.g., when channel is closed by admin)
        /// </summary>
        public async Task ManualCancelTimerAsync(ulong channelId)
        {
            await CancelTimerAsync(channelId);
            _logger.LogInformation($"Manually cancelled timer for channel {channelId}");
        }
    }

    /// <summary>
    /// Represents an active interview timer
    /// </summary>
    public class InterviewTimer
    {
        public ulong ChannelId { get; set; }
        public ulong UserId { get; set; }
        public ulong AdminId { get; set; }
        public DateTimeOffset StartTime { get; set; }
        public DateTimeOffset? FirstWarningSentAt { get; set; }
        public TimerStage Stage { get; set; }
        public bool IsPaused { get; set; }
    }

    /// <summary>
    /// Stages of the interview follow-up timer
    /// </summary>
    public enum TimerStage
    {
        FirstWarning,
        FinalWarning,
        Closed
    }
}
