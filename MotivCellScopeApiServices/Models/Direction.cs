using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class Direction
{
    public ushort DirectionId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Kind { get; set; } = null!;

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    public virtual ICollection<TechnicalCenter> TechnicalCenters { get; set; } = new List<TechnicalCenter>();
}
