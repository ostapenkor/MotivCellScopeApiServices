using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class VAnomaliesOpen
{
    public ulong AnomalyId { get; set; }

    public DateTime DetectedAt { get; set; }

    public uint? CellId { get; set; }

    public DateTime TsFrom { get; set; }

    public DateTime TsTo { get; set; }

    public string AnomalyType { get; set; } = null!;

    public string? KpiCode { get; set; }

    public string Severity { get; set; } = null!;

    public decimal? ObservedValue { get; set; }

    public decimal? ExpectedValue { get; set; }

    public decimal? Score { get; set; }

    public string Status { get; set; } = null!;

    public uint? BtsId { get; set; }

    public string? BtsCode { get; set; }

    public string? BtsFullName { get; set; }

    public ushort? RegionId { get; set; }

    public string? RegionCode { get; set; }
}
