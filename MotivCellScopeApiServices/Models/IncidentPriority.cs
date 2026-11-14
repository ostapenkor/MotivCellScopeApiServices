using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class IncidentPriority
{
    public ushort PriorityId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public sbyte SortOrder { get; set; }

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();
}
