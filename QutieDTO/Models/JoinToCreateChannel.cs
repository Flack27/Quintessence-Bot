using System;
using System.Collections.Generic;

namespace QutieDTO.Models;

public partial class JoinToCreateChannel
{
    public long ChannelId { get; set; }

    public string? ChannelName { get; set; }

    public string? Category { get; set; }
}
