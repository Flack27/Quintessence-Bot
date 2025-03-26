using System;
using System.Collections.Generic;

namespace QutieDTO.Models;

public partial class EventSignup
{
    public long SignUpId { get; set; }

    public long? EventId { get; set; }

    public long? UserId { get; set; }

    public virtual Event? Event { get; set; }

    public virtual User? User { get; set; }
}
