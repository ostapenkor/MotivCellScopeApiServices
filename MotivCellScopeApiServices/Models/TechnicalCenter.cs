using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class TechnicalCenter
{
    public ushort TechCenterId { get; set; }

    public ushort DirectionId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public virtual Direction Direction { get; set; } = null!;

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    public virtual ICollection<NetworkElement> NetworkElements { get; set; } = new List<NetworkElement>();
}
