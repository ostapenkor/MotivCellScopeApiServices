using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class Bt
{
    public uint BtsId { get; set; }

    public string? Address { get; set; }

    public decimal? Lat { get; set; }

    public decimal? Lon { get; set; }

    public string SiteType { get; set; } = null!;

    public virtual NetworkElement Bts { get; set; } = null!;

    public virtual ICollection<Cell> Cells { get; set; } = new List<Cell>();
}
