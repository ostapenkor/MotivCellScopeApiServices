using System;
using System.Collections.Generic;

namespace MotivCellScopeApiServices.Models;

public partial class NetworkElement
{
    public uint Id { get; set; }

    public string ElementType { get; set; } = null!;

    public string Code { get; set; } = null!;

    public string? NetworkElementCode { get; set; }

    public string? FullName { get; set; }

    public string? ShortName { get; set; }

    public string? Description { get; set; }

    public ushort? RegionId { get; set; }

    public ushort? TechCenterId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? StatusSince { get; set; }

    public string? StopReason { get; set; }

    public DateTime? ExpectedRestoreDate { get; set; }

    public DateTime? ActualRestoreDate { get; set; }

    public DateTime? CommissionedAt { get; set; }

    public DateTime? DecommissionedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Bt? Bt { get; set; }

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    public virtual Region? Region { get; set; }

    public virtual TechnicalCenter? TechCenter { get; set; }
}
