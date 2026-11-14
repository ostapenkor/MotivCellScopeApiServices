using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class User
{
    public uint UserId { get; set; }

    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string Role { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();
}
