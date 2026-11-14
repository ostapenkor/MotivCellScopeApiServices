using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class KpiCellHourly
{
    public uint CellId { get; set; }

    public DateTime TsHour { get; set; }

    public ulong ConnAttempt { get; set; }

    public ulong ConnSuccess { get; set; }

    public ulong ConnDrop { get; set; }

    public ulong TrafficDlBytes { get; set; }

    public ulong TrafficUlBytes { get; set; }

    public decimal? CssrPct { get; set; }

    public decimal? DcrPct { get; set; }

    public DateTime ComputedAt { get; set; }

    public virtual Cell Cell { get; set; } = null!;
}
