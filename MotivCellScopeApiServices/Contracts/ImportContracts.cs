using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace MotivCellScopeApiServices.Contracts;
// Оболочка импорта содержит только источник и явно разобранные строки.
// RawValues сохраняются неизменно; данные, которые не удалось разобрать,
// остаются в staging и не исчезают из-за ошибки одной ячейки.
public class ImportBatchInput
{
    [Required, RegularExpression("^[a-fA-F0-9]{64}$")]
    public string FileHash { get; set; } = "";

    [Required, StringLength(255)]
    public string SourceName { get; set; } = "";

    [StringLength(40)]
    public string? TimeZone { get; set; }
    public string? SnapshotAtLocal { get; set; }
    public DateTimeOffset? SnapshotAt { get; set; }

    [Required]
    public List<ImportSheetInput> Sheets { get; set; } = new();

    [Required]
    public List<ImportRowInput> Rows { get; set; } = new();
    public ImportReferenceInput References { get; set; } = new();
}

public class ImportSheetInput
{
    [Required, StringLength(100)]
    public string Name { get; set; } = "";
    public string? SnapshotAtLocal { get; set; }
    public DateTimeOffset? SnapshotAt { get; set; }
}

public class ImportRowInput
{
    [Required, StringLength(100)]
    public string Sheet { get; set; } = "";

    [Range(1, int.MaxValue)]
    public int Row { get; set; }

    [Required, RegularExpression("^(event|overview|deferred|reference)$")]
    public string Kind { get; set; } = "";
    public JsonElement RawValues { get; set; }

    // Важно: MVC не должен отклонить весь пакет из-за одной плохой строки.
    // AccidentInput валидируется вручную в Import, а её raw остаётся в БД.
    [ValidateNever]
    public AccidentInput? Event { get; set; }

    [Required]
    public List<string> Issues { get; set; } = new();
}

public class ImportReferenceInput
{
    public List<ImportCodeInput> Directions { get; set; } = new();
    public List<ImportCodeInput> Centers { get; set; } = new();
}

public class ImportCodeInput
{
    [Required, StringLength(64)]
    public string Code { get; set; } = "";

    [Required, StringLength(450)]
    public string Label { get; set; } = "";

    [StringLength(64)]
    public string? ExternalCode { get; set; }
}

public record ImportResult(int BatchId, bool AlreadyImported, int SourceRows, int EventRows, int RejectedRows, int DeferredRows, int OverviewRows, int IssuesRows);
public class ImportInProgressException : Exception
{
    public ImportInProgressException() : base("Другой импорт ещё выполняется. Повторите запрос позднее.")
    {
    }
}
