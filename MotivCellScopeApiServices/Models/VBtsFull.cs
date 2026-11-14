using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class VBtsFull
{
    public uint BtsId { get; set; }

    public string Code { get; set; } = null!;

    public string? NetworkElementCode { get; set; }

    public string? FullName { get; set; }

    public string? ShortName { get; set; }

    public ushort? RegionId { get; set; }

    public ushort? TechCenterId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? StatusSince { get; set; }

    public string? StopReason { get; set; }

    public DateTime? ExpectedRestoreDate { get; set; }

    public DateTime? ActualRestoreDate { get; set; }

    public DateTime? CommissionedAt { get; set; }

    public DateTime? DecommissionedAt { get; set; }

    public string? Address { get; set; }

    public decimal? Lat { get; set; }

    public decimal? Lon { get; set; }

    public string SiteType { get; set; } = null!;

    public string? RegionCode { get; set; }

    public string? RegionName { get; set; }

    public string? TechCenterCode { get; set; }

    public string? TechCenterName { get; set; }

    public string? DirectionCode { get; set; }
}
