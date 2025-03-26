using System;
using System.Collections.Generic;

namespace QutieDTO.Models;

public partial class Channel
{
    public long ChannelId { get; set; }

    public string? ChannelName { get; set; }

    public bool? IsEventChannel { get; set; }

    public long? RoleId { get; set; }

    public long? GameId { get; set; }

    public virtual Game? Game { get; set; }

    public virtual ICollection<Event> Events { get; set; } = new List<Event>();

    public virtual Role? Role { get; set; }
    public int? SheetTabId { get; set; }
}
