using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class Incident
{
    public ulong IncidentId { get; set; }

    public string? Pw { get; set; }

    public ushort? DirectionId { get; set; }

    public ushort? TechCenterId { get; set; }

    public ushort? PriorityId { get; set; }

    public ushort? NetworkTypeId { get; set; }

    public string? ServElem { get; set; }

    public string? NumbRbs { get; set; }

    public ushort? EquipmentTypeId { get; set; }

    public ushort? AlarmTypeId { get; set; }

    public string? Descr { get; set; }

    public DateTime BeginTime { get; set; }

    public DateTime? EndTime { get; set; }

    public TimeOnly? Akb { get; set; }

    public TimeOnly? Dgu { get; set; }

    public string? Lenght { get; set; }

    public TimeOnly? Prostoy { get; set; }

    public string? CommonProstoy { get; set; }

    public string? AlarmWho { get; set; }

    public string? DescNotice { get; set; }

    public string? AdminDistrict { get; set; }

    public uint? NetworkElementId { get; set; }

    public ushort? RegionId { get; set; }

    public string Severity { get; set; } = null!;

    public string Category { get; set; } = null!;

    public uint? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual AlarmType? AlarmType { get; set; }

    public virtual ICollection<Anomaly> Anomalies { get; set; } = new List<Anomaly>();

    public virtual User? CreatedByUser { get; set; }

    public virtual Direction? Direction { get; set; }

    public virtual EquipmentType? EquipmentType { get; set; }

    public virtual NetworkElement? NetworkElement { get; set; }

    public virtual NetworkType? NetworkType { get; set; }

    public virtual IncidentPriority? Priority { get; set; }

    public virtual Region? Region { get; set; }

    public virtual TechnicalCenter? TechCenter { get; set; }
}
