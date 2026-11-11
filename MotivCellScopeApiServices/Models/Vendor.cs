using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class Vendor
{
    public ushort VendorId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? RatSupported { get; set; }

    public virtual ICollection<Bt> Bts { get; set; } = new List<Bt>();

    public virtual ICollection<KpiCellHourly> KpiCellHourlies { get; set; } = new List<KpiCellHourly>();

    public virtual ICollection<PmCountersHourly> PmCountersHourlies { get; set; } = new List<PmCountersHourly>();
}
