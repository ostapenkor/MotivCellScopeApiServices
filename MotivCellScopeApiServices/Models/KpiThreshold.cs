using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class KpiThreshold
{
    public ulong ThresholdId { get; set; }

    public string KpiCode { get; set; } = null!;

    public ushort? RegionId { get; set; }

    public decimal? MinValue { get; set; }

    public decimal? MaxValue { get; set; }

    public string Direction { get; set; } = null!;

    public string Severity { get; set; } = null!;

    public bool? Enabled { get; set; }

    public virtual Region? Region { get; set; }
}
