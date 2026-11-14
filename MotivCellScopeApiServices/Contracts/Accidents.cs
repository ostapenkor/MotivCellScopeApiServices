using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace MotivCellScopeApiServices.Contracts;
public static class JournalOptions
{
    public static readonly string[] Directions =
    {
        "ХМЯН",
        "СВН",
        "ЮЗН",
        "Вся сеть"
    };
    public static readonly string[] Centers =
    {
        "НБР",
        "АСБ",
        "ИРБ",
        "КРТ",
        "НТГ",
        "НТР",
        "НУРЛ",
        "НВРТ",
        "НУРГ",
        "ОКТ",
        "СЛХ",
        "СОВ",
        "СРГ",
        "ХМН",
        "ЕКБ",
        "КМУ",
        "КРГ",
        "НСР"
    };
    public static readonly string[] Networks =
    {
        "GSM",
        "LTE",
        "LTE/GSM"
    };
    public static readonly string[] AlarmTypes =
    {
        "РРС",
        "ССС",
        "ЭЛ",
        "RBS",
        "МСПД",
        "ТМ",
        "ОПС",
        "ПР",
        "АВР",
        "Аренда"
    };
    public static readonly string[] Roles =
    {
        "admin",
        "duty",
        "engineer"
    };
    public const long MaxTimeSeconds = 3020399; // Предел MariaDB TIME: 838:59:59.
    public static string FormatSeconds(decimal value) => $"{decimal.Floor(value / 3600):00}:{decimal.Floor(value / 60) % 60:00}:{value % 60:00.###}";
}

// Только поля, которые оператор действительно может менять.
// Автор, длительность и общий простой не принимаются от браузера.
public class AccidentInput : IValidatableObject
{
    public static bool TryLocal(string? s, out DateTime result) => DateTime.TryParseExact(s, new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out result) && result.Year >= 1000;
    [JsonIgnore]
    public DateTime StoredBegin => TimeBasis == "utc" ? BeginTime!.Value.UtcDateTime : DateTime.Parse(BeginTimeLocal!, CultureInfo.InvariantCulture);

    [JsonIgnore]
    public DateTime? StoredEnd => TimeBasis == "utc" ? EndTime?.UtcDateTime : EndTimeLocal == null ? null : DateTime.Parse(EndTimeLocal, CultureInfo.InvariantCulture);

    [StringLength(450)]
    public string? Pw { get; set; }

    [Required, StringLength(450)]
    public string Direction { get; set; } = "";

    [StringLength(64)]
    public string Center { get; set; } = "";

    [Range(1, 3)]
    public int? Category { get; set; }

    [StringLength(16)]
    public string? CategoryCode { get; set; }
    public string TimeBasis { get; set; } = "utc";
    public string? BeginTimeLocal { get; set; }
    public string? EndTimeLocal { get; set; }

    [Required, StringLength(450)]
    public string NetworkType { get; set; } = "";

    [Required, StringLength(450)]
    public string ServiceElement { get; set; } = "";
    public int? StationCount { get; set; }
    public bool IsSector { get; set; }

    [StringLength(450)]
    public string? Equipment { get; set; }

    [Required, StringLength(450)]
    public string AlarmType { get; set; } = "";

    [Required, StringLength(450)]
    public string Description { get; set; } = "";
    public DateTimeOffset? BeginTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }

    [Range(typeof(decimal), "0", "99999999999999999")]
    public decimal? BatterySeconds { get; set; }

    [Range(typeof(decimal), "0", "99999999999999999")]
    public decimal? GeneratorSeconds { get; set; }

    [Range(typeof(decimal), "0", "99999999999999999")]
    public decimal? DowntimeSeconds { get; set; }

    [StringLength(450)]
    public string? Notified { get; set; }

    [StringLength(450)]
    public string? Notes { get; set; }

    [Range(0, int.MaxValue)]
    public int Version { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(CategoryCode) && Category == null)
            yield return new("Укажите категорию или исходный код категории.", new[] { nameof(CategoryCode) });
        if (CategoryCode != null && (string.IsNullOrWhiteSpace(CategoryCode) || CategoryCode.Trim() != CategoryCode))
            yield return new("Код категории не должен быть пустым или содержать крайние пробелы.", new[] { nameof(CategoryCode) });
        if (IsSector && StationCount != null || StationCount is < 1 or > 10000)
            yield return new("Количество БС: 1–10000, неизвестно или сектор без количества.", new[] { nameof(StationCount) });
        if (TimeBasis != "utc" && TimeBasis != "source_local")
            yield return new("Допустимое основание времени: utc или source_local.", new[] { nameof(TimeBasis) });
        if (TimeBasis == "utc")
        {
            if (!BeginTime.HasValue || BeginTime.Value.Year < 1000 || BeginTime > DateTimeOffset.UtcNow)
                yield return new("Начало должно быть допустимой датой не позже текущего UTC.", new[] { nameof(BeginTime) });
            if (EndTime.HasValue && (EndTime < BeginTime || EndTime > DateTimeOffset.UtcNow))
                yield return new("Окончание должно быть между началом и текущим UTC.", new[] { nameof(EndTime) });
            if (BeginTimeLocal != null || EndTimeLocal != null)
                yield return new("Для UTC передавайте только даты со смещением.");
        }
        else
        {
            bool valid = TryLocal(BeginTimeLocal, out var begin);
            if (!valid)
                yield return new("Нужно локальное начало yyyy-MM-ddTHH:mm:ss без часового пояса.", new[] { nameof(BeginTimeLocal) });
            if (EndTimeLocal != null && (!TryLocal(EndTimeLocal, out var end) || valid && end < begin))
                yield return new("Локальное окончание должно быть не раньше начала.", new[] { nameof(EndTimeLocal) });
            if (BeginTime != null || EndTime != null)
                yield return new("Не объявляйте исходное локальное время UTC.");
        }
    }
}

public class AccidentDto : AccidentInput
{
    public int Id { get; set; }
    public int CreatedBy { get; set; }
    public string CreatedByName { get; set; } = "";
    public decimal? DurationSeconds { get; set; }
    public decimal? TotalDowntimeSeconds { get; set; }
    public bool NeedsCategoryMapping { get; set; }
    public object? Source { get; set; }
    public DateTimeOffset ServerTimeUtc { get; set; }
}

public class AccidentFilter : IValidatableObject
{
    private readonly DateTime defaultDate = DateTime.UtcNow.Date;
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Direction { get; set; }
    public string? Center { get; set; }

    [Range(1, 3)]
    public int? Category { get; set; }
    public string? NetworkType { get; set; }

    [StringLength(16)]
    public string? CategoryCode { get; set; }
    public string SourceScope { get; set; } = "all";
    public string Status { get; set; } = "all";

    [StringLength(200)]
    public string? Search { get; set; }

    [Range(1, 100000)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 25;

    [BindNever, ValidateNever]
    public DateTime UtcFrom => From == null ? defaultDate.AddDays(-6) : Parse(From).UtcDateTime;

    [BindNever, ValidateNever]
    public DateTime UtcTo => To == null ? defaultDate.AddDays(1) : Parse(To).UtcDateTime;

    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    public static bool HasOffset(string value) => Regex.IsMatch(value, @"(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase);
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        bool validFrom = From == null || (HasOffset(From) && DateTimeOffset.TryParse(From, CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        bool validTo = To == null || (HasOffset(To) && DateTimeOffset.TryParse(To, CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        if ((From == null) != (To == null))
            yield return new("Передайте обе границы периода или не передавайте ни одной.", new[] { nameof(From), nameof(To) });
        if (!validFrom || !validTo)
            yield return new("Нужны даты ISO 8601 с Z или смещением часового пояса.", new[] { nameof(From), nameof(To) });
        if (validFrom && validTo && From != null && To != null && (UtcFrom >= UtcTo || UtcFrom.Year < 1000))
            yield return new("Начало периода должно быть раньше конца.", new[] { nameof(From), nameof(To) });
        if (SourceScope != "all" && SourceScope != "historical" && SourceScope != "live")
            yield return new("Область: all, historical или live.");
        if (Status != "all" && Status != "open" && Status != "closed")
            yield return new("Статус: all, open или closed.", new[] { nameof(Status) });
    }
}

public record JournalUser(int UserId, string Username, string Role, bool Active, int Version);
public record AccidentPage(int Total, int Page, int PageSize, DateTimeOffset ServerTimeUtc, object Summary, List<AccidentDto> Items);
public class CreateJournalUser
{
    [Required, RegularExpression("^[A-Za-z0-9_]{3,40}$")]
    public string Username { get; set; } = "";

    [Required, MinLength(8), MaxLength(72)]
    public string Password { get; set; } = "";

    [Required]
    public string Role { get; set; } = "engineer";
}

public class UpdateJournalAccess
{
    [Required]
    public string Role { get; set; } = "";
    public bool Active { get; set; }

    [Range(1, int.MaxValue)]
    public int Version { get; set; }
}

// Не допускает неоднозначных JSON-дат без часового пояса. На выходе всегда UTC.
public class UtcDateConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Дата должна быть строкой ISO 8601.");
        string? value = reader.GetString();
        if (value == null || !AccidentFilter.HasOffset(value) || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new JsonException("Укажите ISO 8601 с Z или смещением часового пояса.");
        return date.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value.UtcDateTime.ToString("O"));
}
