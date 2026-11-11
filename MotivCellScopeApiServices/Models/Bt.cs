using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class Bt
{
    public uint BtsId { get; set; }

    public ushort RegionId { get; set; }

    public ushort VendorId { get; set; }

    public string Code { get; set; } = null!;

    public string? Address { get; set; }

    public decimal? Lat { get; set; }

    public decimal? Lon { get; set; }

    public string SiteType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? StatusSince { get; set; }

    public virtual ICollection<Cell> Cells { get; set; } = new List<Cell>();

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    public virtual Region Region { get; set; } = null!;

    public virtual Vendor Vendor { get; set; } = null!;
}
