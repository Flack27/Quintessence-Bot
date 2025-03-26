using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QutieDTO.Models;
using System.Data;

namespace QutieDAL.DAL
{
    public class RaidHelperManagerDAL
    {
        private readonly IDbContextFactory<QutieDataTestContext> _contextFactory;
        private readonly ILogger<RaidHelperManagerDAL> _logger;

        public RaidHelperManagerDAL(IDbContextFactory<QutieDataTestContext> contextFactory, ILogger<RaidHelperManagerDAL> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }


        public async Task<List<Channel>> GetEventChannels()
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var configurations = await context.Channels.Include(r => r.Role).Include(g => g.Game).Where(c => c.IsEventChannel == true).ToListAsync();
                return configurations;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting events");
                return new List<Channel>();
            }
        }


        public async Task<bool> UpsertEvent(Event evnt)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();

                var existingEvent = await context.Events
                    .FirstOrDefaultAsync(e => e.EventId == evnt.EventId);

                if (existingEvent != null)
                {
                    // Update existing event
                    existingEvent.Title = evnt.Title;
                    existingEvent.Date = evnt.Date;

                    // Handle signups (this might need more complex logic depending on your requirements)
                    foreach (var signup in evnt.EventSignups)
                    {
                        var existingSignup = await context.EventSignups
                            .FirstOrDefaultAsync(s => s.EventId == evnt.EventId && s.UserId == signup.UserId);

                        if (existingSignup == null)
                        {
                            context.EventSignups.Add(new EventSignup
                            {
                                EventId = evnt.EventId,
                                UserId = signup.UserId,
                                SignUpId = signup.SignUpId
                            });
                        }
                    }
                }
                else
                {
                    // Create new event
                    context.Events.Add(evnt);
                }

                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error upserting event {evnt.EventId}");
                return false;
            }
        }

        public async Task<List<Event>> GetEventsFromLast7Days()
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();

                var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);

                var events = await context.Events
                    .Include(e => e.Channel)
                    .ThenInclude(c => c.Game)
                    .Include(e => e.EventSignups)
                    .Where(e => e.Date >= sevenDaysAgo)
                    .ToListAsync();

                return events;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting events from last 7 days");
                return new List<Event>();
            }
        }


        public async Task<bool> AddSignup(long eventId, long userId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();

                // Check if signup already exists
                var existingSignup = await context.EventSignups
                    .FirstOrDefaultAsync(s => s.EventId == eventId && s.UserId == userId);

                if (existingSignup != null)
                {
                    // Signup already exists
                    return true;
                }

                // Create new signup
                var newSignup = new EventSignup
                {
                    EventId = eventId,
                    UserId = userId
                };

                context.EventSignups.Add(newSignup);
                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding signup for user {userId} to event {eventId}");
                return false;
            }
        }

        public async Task<bool> RemoveSignup(long eventId, long userId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();

                var signup = await context.EventSignups
                    .FirstOrDefaultAsync(s => s.EventId == eventId && s.UserId == userId);

                if (signup != null)
                {
                    context.EventSignups.Remove(signup);
                    await context.SaveChangesAsync();
                    return true;
                }

                // Signup not found
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing signup for user {userId} from event {eventId}");
                return false;
            }
        }

        public async Task<long?> GetSignupId(long eventId, long userId)
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();

                var signup = await context.EventSignups
                    .FirstOrDefaultAsync(s => s.EventId == eventId && s.UserId == userId);

                return signup?.SignUpId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting signup ID for user {userId} in event {eventId}");
                return null;
            }
        }
    }
}
