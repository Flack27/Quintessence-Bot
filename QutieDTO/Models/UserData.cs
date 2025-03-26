using System;
using System.Collections.Generic;

namespace QutieDTO.Models;

public partial class UserData
{
    public long UserId { get; set; }

    public int MessageXp { get; set; }

    public int MessageLevel { get; set; }

    public int MessageRequiredXp { get; set; }

    public int MessageCount { get; set; }

    public int VoiceXp { get; set; }

    public int VoiceLevel { get; set; }

    public int VoiceRequiredXp { get; set; }

    public decimal TotalVoiceTime { get; set; }

    public int StoredMessageXp { get; set; }

    public int StoredVoiceXp { get; set; }

    public double Karma { get; set; }

    public virtual User User { get; set; } = null!;
}
