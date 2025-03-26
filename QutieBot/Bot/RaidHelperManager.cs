using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QutieBot.Bot.GoogleSheets;
using QutieDAL.DAL;
using QutieDTO.Models;
using System;
using System.Net;
using System.Text;

namespace QutieBot.Bot
{
    public class RaidHelperManager
    {
        private readonly RaidHelperManagerDAL _dal;
        private GoogleSheetsFacade _googleSheets;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<RaidHelperManager> _logger;

        public RaidHelperManager(RaidHelperManagerDAL dal , GoogleSheetsFacade events, IHttpClientFactory httpClientFactory, ILogger<RaidHelperManager> logger)
        {
            _dal = dal;
            _googleSheets = events;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task UpdateEvents()
        {
            List<Channel> eventChannels = await _dal.GetEventChannels();

            if (eventChannels == null || !eventChannels.Any())
            {
                _logger.LogWarning("No event channels found to update");
                return;
            }

            using var semaphore = new SemaphoreSlim(3);

            var tasks = eventChannels.Select(async channel =>
            {
                try
                {
                    await semaphore.WaitAsync();

                    _logger.LogInformation($"Processing events for channel {channel.ChannelName}");
                    var events = await GetRaidHelperEvents(channel.ChannelId.ToString());

                    if (events.Count == 0)
                    {
                        _logger.LogInformation($"No events found for channel {channel.ChannelName}");
                        return;
                    }

                    var now = DateTime.UtcNow;

                    var futureEvents = events
                        .Where(e => DateTimeOffset.FromUnixTimeSeconds(e.startTime).UtcDateTime > now)
                        .ToList();

                    _logger.LogInformation($"Found {events.Count} events, {futureEvents.Count} are in the future and will be processed");

                    if (futureEvents.Count == 0)
                    {
                        _logger.LogInformation($"No future events found for channel {channel.ChannelName}");
                        return;
                    }

                    var eventList = events
                        .Select(eventData => new Event
                        {
                            EventId = (long)eventData.id,
                            ChannelId = (long)eventData.channelId,
                            Title = eventData.title,
                            Date = DateTimeOffset.FromUnixTimeSeconds(eventData.startTime).UtcDateTime,
                            EventSignups = (eventData.signUps ?? new List<SignUpData>())
                                .Where(signup => !IsIgnoredSpecName(signup.specName)
                                    && !IsIgnoredSpecName(signup.className)
                                    && !IsIgnoredSpecName(signup.roleName))
                                .Select(signup => new EventSignup
                                {
                                    SignUpId = (long)signup.id,
                                    UserId = (long)signup.userId,
                                })
                                .ToList() 
                        })
                        .ToList();

                    foreach (var evt in eventList)
                    {
                        await _dal.UpsertEvent(evt);
                    }

                    await _googleSheets.SyncEventsAsync(eventList, channel);
                    _logger.LogInformation($"Successfully processed {eventList.Count} events for channel {channel.ChannelName}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing events for channel {channel.ChannelName}");
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);
        }


        private async Task<T> ExecuteWithRateLimitHandling<T>(Func<HttpClient, Task<T>> apiCall)
        {
            using (var httpClient = _httpClientFactory.CreateClient("RaidHelper"))
            {
                try
                {
                    return await apiCall(httpClient);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    TimeSpan retryAfter = TimeSpan.FromSeconds(60);
                    if (ex.Data.Contains("RetryAfter") && ex.Data["RetryAfter"] is TimeSpan span)
                    {
                        retryAfter = span;
                    }

                    _logger.LogWarning($"Rate limit exceeded. Retrying after {retryAfter.TotalSeconds} seconds.");
                    await Task.Delay(retryAfter);
                    return await ExecuteWithRateLimitHandling(apiCall);
                }
            }
        }

        public async Task<List<EventData>> GetRaidHelperEvents(string channelId)
        {
            return await ExecuteWithRateLimitHandling(async (httpClient) =>
            {
                httpClient.DefaultRequestHeaders.Add("ChannelFilter", channelId);
                httpClient.DefaultRequestHeaders.Add("IncludeSignUps", "true");

                var response = await httpClient.GetAsync("https://raid-helper.dev/api/v3/servers/1137802734284832910/events");
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(jsonString);

                if (json.ContainsKey("postedEvents"))
                {
                    return JsonConvert.DeserializeObject<List<EventData>>(json["postedEvents"].ToString()) ?? new List<EventData>();
                }

                return new List<EventData>();
            });
        }

        public async Task<EventData> GetRaidHelperEvent(string eventId)
        {
            return await ExecuteWithRateLimitHandling(async (httpClient) =>
            {
                var response = await httpClient.GetAsync($"https://raid-helper.dev/api/v2/events/{eventId}");
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<EventData>(jsonString) ?? new EventData();
            });
        }

        public async Task<bool> RemoveAttendanceFromDb(long eventId, long userId)
        {
            return await _dal.RemoveSignup(eventId, userId);
        }

        public async Task<bool> AddAttendanceInDb(long eventId, long userId)
        {
            return await _dal.AddSignup(eventId, userId);
        }


        public async Task<List<Event>> GetEventsFromDb()
        {
            return await _dal.GetEventsFromLast7Days();
        }

        private static readonly HashSet<string> _ignoredSpecNames = new HashSet<string>
        {
            "Tentative",
            "Bench",
            "Declined",
            "Maybe",
            "Absence",
            "No"
        };

        public bool IsIgnoredSpecName(string specName)
        {
            return _ignoredSpecNames.Contains(specName);
        }
    }

    public class EventData
    {
        public ulong id { get; set; }
        public ulong channelId { get; set; }
        public string title { get; set; }
        public int startTime { get; set; }
        public List<SignUpData>? signUps { get; set; }
    }

    public class SignUpData
    {
        public ulong id { get; set; }
        public ulong userId { get; set; }
        public string name { get; set; }
        public string specName { get; set; }
        public string className { get; set; }
        public string roleName { get; set; }
    }

    public class DeleteResponse
    {
        public string status { get; set; }
    }
}
