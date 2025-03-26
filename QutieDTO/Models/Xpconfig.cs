using System;
using System.Collections.Generic;

namespace QutieDTO.Models;

public partial class Xpconfig
{
    public int ConfigId { get; set; }

    public int MessageMinXp { get; set; }

    public int MessageMaxXp { get; set; }

    public int MessageCooldown { get; set; }

    public int VoiceMinXp { get; set; }

    public int VoiceMaxXp { get; set; }

    public int VoiceCooldown { get; set; }
}
