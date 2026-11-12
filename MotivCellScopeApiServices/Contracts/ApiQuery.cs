using System.ComponentModel.DataAnnotations;

namespace MotivCellScopeApiServices.Contracts;

public class PeriodQuery : IValidatableObject
{
    [Required] public DateTimeOffset? From { get; set; }
    [Required] public DateTimeOffset? To { get; set; }
    [Range(1, ushort.MaxValue)] public ushort? RegionId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (From.HasValue && To.HasValue && From >= To)
            yield return new ValidationResult("from must be before to", new[] { nameof(From) });
    }
}

public class IncidentsQuery : PeriodQuery
{
    [RegularExpression("^(power|transport|hardware|software|planned|external)$")]
    public string? Category { get; set; }
    [RegularExpression("^(low|medium|high|critical)$")]
    public string? Severity { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 200)] public int PageSize { get; set; } = 50;
}

public record VendorDto(ushort VendorId, string Code, string Name, string? RatSupported);
public record PageDto<T>(int Total, int Page, int PageSize, IReadOnlyList<T> Items);
public record CellReportDto(uint CellId, string BtsCode, string Sector, string Region,
    decimal? Cssr, decimal? Dcr, decimal? Availability, decimal TrafficGb, int Anomalies);
