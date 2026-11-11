using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class Incident
{
    public ulong IncidentId { get; set; }

    public ushort RegionId { get; set; }

    public uint? BtsId { get; set; }

    public string Category { get; set; } = null!;

    public string Severity { get; set; } = null!;

    public DateTime StartedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public string? Description { get; set; }

    public virtual ICollection<Anomaly> Anomalies { get; set; } = new List<Anomaly>();

    public virtual Bt? Bts { get; set; }

    public virtual Region Region { get; set; } = null!;
}
