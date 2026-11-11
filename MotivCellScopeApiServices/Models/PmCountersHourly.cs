using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class PmCountersHourly
{
    public ushort VendorId { get; set; }

    public uint CellId { get; set; }

    public DateTime TsHour { get; set; }

    public ushort MetricId { get; set; }

    public decimal Value { get; set; }

    public virtual Cell Cell { get; set; } = null!;

    public virtual MetricCatalog Metric { get; set; } = null!;

    public virtual Vendor Vendor { get; set; } = null!;
}
