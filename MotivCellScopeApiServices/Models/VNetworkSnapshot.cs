using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class VNetworkSnapshot
{
    public ushort RegionId { get; set; }

    public string RegionCode { get; set; } = null!;

    public string RegionName { get; set; } = null!;

    public long BtsTotal { get; set; }

    public decimal? BtsActive { get; set; }

    public decimal? BtsProblem { get; set; }

    public decimal? BtsMaintenance { get; set; }

    public decimal? BtsDecommissioned { get; set; }

    public long CellTotal { get; set; }
}
