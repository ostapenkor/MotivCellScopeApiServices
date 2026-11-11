using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class MetricCatalog
{
    public ushort MetricId { get; set; }

    public string MetricCode { get; set; } = null!;

    public string MetricName { get; set; } = null!;

    public string MetricKind { get; set; } = null!;

    public string? Unit { get; set; }

    public virtual ICollection<PmCountersHourly> PmCountersHourlies { get; set; } = new List<PmCountersHourly>();
}
