using System;
using System.Collections.Generic;

namespace QutieDTO.Models;

public partial class LevelToRoleMessage
{
    public int Level { get; set; }

    public long RoleId { get; set; }

    public virtual Role Role { get; set; }
}
