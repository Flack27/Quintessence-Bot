using System;
using System.Collections.Generic;

namespace QutieDTO.Models;

public partial class ReactionRoleConfig
{
    public int ConfigId { get; set; }

    public string? Emoji { get; set; }

    public long? ModeratorRoleId { get; set; }

    public long? VerificationRoleId { get; set; }

    public long? OnlyOneChannelId { get; set; }

}
