using System;
using System.Collections.Generic;

namespace QutieDTO.Models;

public partial class Event
{
    public long EventId { get; set; }

    public long ChannelId { get; set; }

    public string? Title { get; set; }

    public DateTime Date { get; set; }

    public virtual Channel? Channel { get; set; }

    public virtual ICollection<EventSignup> EventSignups { get; set; } = new List<EventSignup>();
}
