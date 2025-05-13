using System;
using System.Collections.Generic;

namespace QutieDTO.Models;

public partial class User
{
    public long UserId { get; set; }

    public string? UserName { get; set; }

    public string? DisplayName { get; set; }

    public string? Avatar { get; set; }

    public bool InGuild { get; set; }

    public string? Description { get; set; }

    public virtual ICollection<EventSignup> EventSignups { get; set; } = new List<EventSignup>();

    public virtual UserData? UserData { get; set; }

    public virtual ICollection<Role> Roles { get; set; } = new List<Role>();
}
