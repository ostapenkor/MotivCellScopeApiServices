using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class VIncidentsFull
{
    public ulong IncidentId { get; set; }

    public string? Pw { get; set; }

    public string? DirectionCode { get; set; }

    public string? TechCenterCode { get; set; }

    public string? PriorityCode { get; set; }

    public string? NetworkTypeCode { get; set; }

    public string? EquipmentTypeCode { get; set; }

    public string? AlarmTypeCode { get; set; }

    public string? NetworkElementType { get; set; }

    public string? NetworkElementName { get; set; }

    public string? ServElem { get; set; }

    public string? NumbRbs { get; set; }

    public string? Descr { get; set; }

    public string? DescNotice { get; set; }

    public DateTime BeginTime { get; set; }

    public DateTime? EndTime { get; set; }

    public TimeOnly? Akb { get; set; }

    public TimeOnly? Dgu { get; set; }

    public string? Lenght { get; set; }

    public TimeOnly? Prostoy { get; set; }

    public string? CommonProstoy { get; set; }

    public string? AlarmWho { get; set; }

    public string? AdminDistrict { get; set; }

    public ushort? RegionId { get; set; }

    public string Severity { get; set; } = null!;

    public string Category { get; set; } = null!;

    public uint? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
