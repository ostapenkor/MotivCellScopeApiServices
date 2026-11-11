using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class User
{
    public uint UserId { get; set; }

    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
