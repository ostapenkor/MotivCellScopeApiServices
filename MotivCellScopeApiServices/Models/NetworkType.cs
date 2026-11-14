using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class NetworkType
{
    public ushort NetworkTypeId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();
}
