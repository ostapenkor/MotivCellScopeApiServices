using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class Cell
{
    public uint CellId { get; set; }

    public uint BtsId { get; set; }

    public ushort LocalCellId { get; set; }

    public string Sector { get; set; } = null!;

    public virtual ICollection<Anomaly> Anomalies { get; set; } = new List<Anomaly>();

    public virtual Bt Bts { get; set; } = null!;

    public virtual ICollection<KpiCellHourly> KpiCellHourlies { get; set; } = new List<KpiCellHourly>();

    public virtual ICollection<PmCountersHourly> PmCountersHourlies { get; set; } = new List<PmCountersHourly>();
}
