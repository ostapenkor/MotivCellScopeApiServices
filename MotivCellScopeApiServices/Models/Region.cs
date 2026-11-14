using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class Region
{
    public ushort RegionId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public short TzOffsetMin { get; set; }

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    public virtual ICollection<KpiThreshold> KpiThresholds { get; set; } = new List<KpiThreshold>();

    public virtual ICollection<NetworkElement> NetworkElements { get; set; } = new List<NetworkElement>();
}
