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
    public static string FormatSeconds(long value) => $"{value / 3600:00}:{value / 60 % 60:00}:{value % 60:00}";
}

// Только поля, которые оператор действительно может менять.
// Автор, длительность и общий простой не принимаются от браузера.
public class AccidentInput : IValidatableObject
{
    [StringLength(450)]
    public string? Pw { get; set; }

    [Required, StringLength(450)]
    public string Direction { get; set; } = "";

    [Required, StringLength(450)]
    public string Center { get; set; } = "";

    [Range(1, 3)]
    public int Category { get; set; }

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

    [Required]
    public DateTimeOffset? BeginTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }

    [Range(0, JournalOptions.MaxTimeSeconds)]
    public long BatterySeconds { get; set; }

    [Range(0, JournalOptions.MaxTimeSeconds)]
    public long GeneratorSeconds { get; set; }

    [Range(0, JournalOptions.MaxTimeSeconds)]
    public long DowntimeSeconds { get; set; }

    [StringLength(450)]
    public string? Notified { get; set; }

    [StringLength(450)]
    public string? Notes { get; set; }

    [Range(0, int.MaxValue)]
    public int Version { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!JournalOptions.Directions.Contains(Direction))
            yield return new("Выберите направление из списка.", new[] { nameof(Direction) });
        if (!JournalOptions.Centers.Contains(Center))
            yield return new("Выберите ТЦ из списка.", new[] { nameof(Center) });
        if (!JournalOptions.Networks.Contains(NetworkType))
            yield return new("Выберите тип сети из списка.", new[] { nameof(NetworkType) });
        if (!JournalOptions.AlarmTypes.Contains(AlarmType))
            yield return new("Выберите тип аварии из списка.", new[] { nameof(AlarmType) });
        if (IsSector ? StationCount != null : StationCount is null or < 1 or > 10000)
            yield return new("Укажите 1–10000 БС или сектор без количества полных БС.", new[] { nameof(StationCount) });
        var now = DateTimeOffset.UtcNow;
        if (BeginTime.HasValue && (BeginTime.Value.Year < 1000 || BeginTime > now))
            yield return new("Начало должно быть допустимой датой не позже текущего UTC.", new[] { nameof(BeginTime) });
        if (EndTime.HasValue && (EndTime < BeginTime || EndTime > now || EndTime.Value.Year < 1000))
            yield return new("Окончание должно быть между началом и текущим UTC.", new[] { nameof(EndTime) });
        if (!IsSector && StationCount.HasValue && DowntimeSeconds >= 0 && DowntimeSeconds * (long)StationCount > JournalOptions.MaxTimeSeconds)
            yield return new("Общий простой превышает предел TIME 838:59:59. Разделите событие или согласуйте другой тип хранения.", new[] { nameof(DowntimeSeconds) });
        if (!string.IsNullOrWhiteSpace(Pw) && (!Uri.TryCreate(Pw, UriKind.Absolute, out var url) || (url.Scheme != "http" && url.Scheme != "https")))
            yield return new("PW/NTT должен быть полной ссылкой http или https.", new[] { nameof(Pw) });
    }
}

public class AccidentDto : AccidentInput
{
    public int Id { get; set; }
    public int CreatedBy { get; set; }
    public string CreatedByName { get; set; } = "";
    public long DurationSeconds { get; set; }
    public long? TotalDowntimeSeconds { get; set; }
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
        if (!string.IsNullOrEmpty(Direction) && !JournalOptions.Directions.Contains(Direction))
            yield return new("Неверное направление.", new[] { nameof(Direction) });
        if (!string.IsNullOrEmpty(Center) && !JournalOptions.Centers.Contains(Center))
            yield return new("Неверный ТЦ.", new[] { nameof(Center) });
        if (!string.IsNullOrEmpty(NetworkType) && !JournalOptions.Networks.Contains(NetworkType))
            yield return new("Неверный тип сети.", new[] { nameof(NetworkType) });
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
