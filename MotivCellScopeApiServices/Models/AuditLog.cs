using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class AuditLog
{
    public ulong AuditId { get; set; }

    public DateTime Ts { get; set; }

    public uint? UserId { get; set; }

    public string Username { get; set; } = null!;

    public string? Role { get; set; }

    public string Action { get; set; } = null!;

    public string? EntityType { get; set; }

    public ulong? EntityId { get; set; }

    public string? IpAddress { get; set; }

    public bool? Success { get; set; }

    public virtual User? User { get; set; }
}
