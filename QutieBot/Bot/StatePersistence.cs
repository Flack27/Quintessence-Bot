using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace QutieBot.Bot
{
    /// <summary>
    /// Handles persistence of in-memory state to a JSON file
    /// </summary>
    public class StatePersistence
    {
        private readonly string _filePath;
        private readonly ILogger<StatePersistence> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly JsonSerializerOptions _jsonOptions;

        // Debounce timer for save operations
        private CancellationTokenSource _debounceCts;
        private readonly TimeSpan _debounceDelay = TimeSpan.FromSeconds(5);

        public StatePersistence(ILogger<StatePersistence> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Use /app/data in Docker, or local directory in development
            var dataDir = Environment.GetEnvironmentVariable("BOT_DATA_PATH") ?? 
                          Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            _filePath = Path.Combine(dataDir, "bot_state.json");

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() }
            };

            _logger.LogInformation($"State persistence initialized with path: {_filePath}");
        }

        /// <summary>
        /// Saves state immediately
        /// </summary>
        public async Task SaveStateAsync(BotState state)
        {
            await _lock.WaitAsync();
            try
            {
                var json = JsonSerializer.Serialize(state, _jsonOptions);
                await File.WriteAllTextAsync(_filePath, json);
                _logger.LogDebug("Bot state saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save bot state");
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Saves state with debouncing (waits for activity to settle before saving)
        /// Use this for frequent updates to avoid excessive disk writes
        /// </summary>
        public void SaveStateDebounced(BotState state)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_debounceDelay, _debounceCts.Token);
                    await SaveStateAsync(state);
                }
                catch (TaskCanceledException)
                {
                    // Debounce cancelled, new save incoming
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in debounced save");
                }
            });
        }

        /// <summary>
        /// Loads state from disk
        /// </summary>
        public async Task<BotState> LoadStateAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(_filePath))
                {
                    _logger.LogInformation("No existing state file found, starting fresh");
                    return new BotState();
                }

                var json = await File.ReadAllTextAsync(_filePath);
                var state = JsonSerializer.Deserialize<BotState>(json, _jsonOptions);

                if (state == null)
                {
                    _logger.LogWarning("State file was empty or invalid, starting fresh");
                    return new BotState();
                }

                _logger.LogInformation($"Loaded state: {state.JtcCreatedChannels?.Count ?? 0} JTC mappings, " +
                                       $"{state.InterviewTimers?.Count ?? 0} interview timers, " +
                                       $"{state.ActiveInterviews?.Count ?? 0} active interviews, " +
                                       $"{state.VoiceSessions?.Count ?? 0} voice sessions");

                return state;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse state file, starting fresh");
                return new BotState();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load bot state, starting fresh");
                return new BotState();
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Deletes the state file (for testing or reset)
        /// </summary>
        public async Task ClearStateAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                    _logger.LogInformation("State file deleted");
                }
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    #region State Models

    /// <summary>
    /// Root state object containing all persisted data
    /// </summary>
    public class BotState
    {
        /// <summary>
        /// When this state was last saved
        /// </summary>
        public DateTime LastSaved { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// JoinToCreate: Maps target channel ID -> list of created channel IDs
        /// </summary>
        public Dictionary<ulong, List<ulong>> JtcCreatedChannels { get; set; } = new();

        /// <summary>
        /// Interview follow-up timers: Maps channel ID -> timer state
        /// </summary>
        public Dictionary<ulong, InterviewTimerState> InterviewTimers { get; set; } = new();

        /// <summary>
        /// Active interviews: Maps user ID -> interview data
        /// </summary>
        public Dictionary<ulong, InterviewDataState> ActiveInterviews { get; set; } = new();

        /// <summary>
        /// Voice sessions: Maps user ID -> session state
        /// </summary>
        public Dictionary<ulong, VoiceSessionState> VoiceSessions { get; set; } = new();
    }

    /// <summary>
    /// Persisted state for an interview timer
    /// </summary>
    public class InterviewTimerState
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
    /// Persisted state for an active interview
    /// </summary>
    public class InterviewDataState
    {
        public ulong UserId { get; set; }
        public ulong ChannelId { get; set; }
        public long SubmissionId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>
    /// Persisted state for a voice session
    /// </summary>
    public class VoiceSessionState
    {
        public DateTime StartTime { get; set; }
        public DateTime LastCheckpoint { get; set; }
        public ulong ChannelId { get; set; }
    }

    #endregion
}
