using Microsoft.Extensions.Logging;
using QutieBot.Bot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QutieBot
{
    /// <summary>
    /// Coordinates state persistence across all bot services.
    /// Inject this into services that need state persistence.
    /// </summary>
    public class StateManager
    {
        private readonly StatePersistence _persistence;
        private readonly ILogger<StateManager> _logger;

        private BotState _currentState;
        private bool _initialized = false;

        public StateManager(StatePersistence persistence, ILogger<StateManager> logger)
        {
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _currentState = new BotState();
        }

        /// <summary>
        /// Loads state from disk. Call this once during bot startup.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_initialized)
            {
                _logger.LogWarning("StateManager already initialized");
                return;
            }

            _currentState = await _persistence.LoadStateAsync();
            _initialized = true;
            _logger.LogInformation("StateManager initialized");
        }

        /// <summary>
        /// Saves current state to disk immediately
        /// </summary>
        public async Task SaveAsync()
        {
            _currentState.LastSaved = DateTime.UtcNow;
            await _persistence.SaveStateAsync(_currentState);
        }

        /// <summary>
        /// Saves current state with debouncing (for frequent updates)
        /// </summary>
        public void SaveDebounced()
        {
            _currentState.LastSaved = DateTime.UtcNow;
            _persistence.SaveStateDebounced(_currentState);
        }

        #region JoinToCreate State

        /// <summary>
        /// Gets JTC created channels state for initialization
        /// </summary>
        public Dictionary<ulong, List<ulong>> GetJtcCreatedChannels()
        {
            return _currentState.JtcCreatedChannels ?? new Dictionary<ulong, List<ulong>>();
        }

        /// <summary>
        /// Updates JTC state from the service's current dictionary
        /// </summary>
        public void UpdateJtcCreatedChannels(Dictionary<ulong, List<ulong>> channels)
        {
            _currentState.JtcCreatedChannels = channels.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToList()
            );
            SaveDebounced();
        }

        /// <summary>
        /// Adds a created channel to JTC tracking
        /// </summary>
        public void AddJtcCreatedChannel(ulong targetChannelId, ulong createdChannelId)
        {
            if (!_currentState.JtcCreatedChannels.ContainsKey(targetChannelId))
            {
                _currentState.JtcCreatedChannels[targetChannelId] = new List<ulong>();
            }

            if (!_currentState.JtcCreatedChannels[targetChannelId].Contains(createdChannelId))
            {
                _currentState.JtcCreatedChannels[targetChannelId].Add(createdChannelId);
                SaveDebounced();
            }
        }

        /// <summary>
        /// Removes a created channel from JTC tracking
        /// </summary>
        public void RemoveJtcCreatedChannel(ulong targetChannelId, ulong createdChannelId)
        {
            if (_currentState.JtcCreatedChannels.TryGetValue(targetChannelId, out var channels))
            {
                if (channels.Remove(createdChannelId))
                {
                    SaveDebounced();
                }
            }
        }

        #endregion

        #region Interview Timer State

        /// <summary>
        /// Gets all persisted interview timers for initialization
        /// </summary>
        public Dictionary<ulong, InterviewTimerState> GetInterviewTimers()
        {
            return _currentState.InterviewTimers ?? new Dictionary<ulong, InterviewTimerState>();
        }

        /// <summary>
        /// Updates or adds an interview timer
        /// </summary>
        public void UpdateInterviewTimer(ulong channelId, InterviewTimerState timer)
        {
            _currentState.InterviewTimers[channelId] = timer;
            SaveDebounced();
        }

        /// <summary>
        /// Removes an interview timer
        /// </summary>
        public void RemoveInterviewTimer(ulong channelId)
        {
            if (_currentState.InterviewTimers.Remove(channelId))
            {
                SaveDebounced();
            }
        }

        #endregion

        #region Active Interview State

        /// <summary>
        /// Gets all persisted active interviews for initialization
        /// </summary>
        public Dictionary<ulong, InterviewDataState> GetActiveInterviews()
        {
            return _currentState.ActiveInterviews ?? new Dictionary<ulong, InterviewDataState>();
        }

        /// <summary>
        /// Updates or adds an active interview
        /// </summary>
        public void UpdateActiveInterview(ulong userId, InterviewDataState interview)
        {
            _currentState.ActiveInterviews[userId] = interview;
            SaveDebounced();
        }

        /// <summary>
        /// Removes an active interview
        /// </summary>
        public void RemoveActiveInterview(ulong userId)
        {
            if (_currentState.ActiveInterviews.Remove(userId))
            {
                SaveDebounced();
            }
        }

        #endregion

        #region Voice Session State

        /// <summary>
        /// Gets all persisted voice sessions for initialization
        /// </summary>
        public Dictionary<ulong, VoiceSessionState> GetVoiceSessions()
        {
            return _currentState.VoiceSessions ?? new Dictionary<ulong, VoiceSessionState>();
        }

        /// <summary>
        /// Updates or adds a voice session
        /// </summary>
        public void UpdateVoiceSession(ulong userId, VoiceSessionState session)
        {
            _currentState.VoiceSessions[userId] = session;
            SaveDebounced();
        }

        /// <summary>
        /// Removes a voice session
        /// </summary>
        public void RemoveVoiceSession(ulong userId)
        {
            if (_currentState.VoiceSessions.Remove(userId))
            {
                SaveDebounced();
            }
        }

        /// <summary>
        /// Bulk update voice sessions from ConcurrentDictionary
        /// </summary>
        public void UpdateAllVoiceSessions(ConcurrentDictionary<ulong, VoiceSessionState> sessions)
        {
            _currentState.VoiceSessions = sessions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            SaveDebounced();
        }

        #endregion
    }
}
