using System.Text.Json;
using Omnichannel.Domain.Ai;
using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Automation;

/// <summary>
/// General-purpose per-tenant business hours + holidays (PRD §72) — one row per tenant. Distinct
/// from <see cref="AiAutoReplySettings"/>'s own business-hours config (Phase 12, which gates AI
/// auto-reply specifically): this is the canonical "is the business open right now" concept that
/// escalation rules and notifications key off of. Reuses <see cref="BusinessHoursWindow"/> (the
/// same small public record Phase 12 already defined) rather than inventing a second type, but
/// keeps its own JSON (de)serialization rather than retrofitting Phase 12's already-shipped,
/// already-tested entity to share it — a deliberate, small, contained duplication over a riskier
/// cross-phase refactor (see ADR-0023).
/// </summary>
public sealed class TenantBusinessHours : ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string? BusinessHoursJson { get; private set; }
    public string HolidaysJson { get; private set; } = "[]";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TenantBusinessHours()
    {
    }

    public static TenantBusinessHours CreateDefault(Guid tenantId, DateTimeOffset now)
        => new()
        {
            TenantId = tenantId,
            BusinessHoursJson = null,
            HolidaysJson = "[]",
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void Configure(
        IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>>? businessHours,
        IReadOnlyCollection<DateOnly> holidays,
        DateTimeOffset now)
    {
        BusinessHoursJson = businessHours is null or { Count: 0 } ? null : Serialize(businessHours);
        HolidaysJson = JsonSerializer.Serialize(holidays.Distinct().OrderBy(d => d).Select(d => d.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
        UpdatedAt = now;
    }

    public IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>> GetBusinessHours()
        => Deserialize(BusinessHoursJson);

    public IReadOnlyList<DateOnly> GetHolidays()
    {
        try
        {
            var raw = JsonSerializer.Deserialize<List<string>>(HolidaysJson) ?? [];
            return raw.Select(DateOnly.Parse).ToList();
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return [];
        }
    }

    /// <summary>Holidays always close the business regardless of the weekly schedule; otherwise
    /// falls back to the same conservative "unconfigured = closed" rule as Phase 12's own
    /// business-hours check.</summary>
    public bool IsOpenNow(DateTimeOffset nowUtc, string tenantTimeZoneId)
    {
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenantTimeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }

        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        if (GetHolidays().Contains(DateOnly.FromDateTime(localNow.DateTime)))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(BusinessHoursJson))
        {
            return false;
        }

        var schedule = Deserialize(BusinessHoursJson);
        if (!schedule.TryGetValue(localNow.DayOfWeek, out var windows))
        {
            return false;
        }

        var timeOfDay = TimeOnly.FromTimeSpan(localNow.TimeOfDay);
        return windows.Any(w => timeOfDay >= w.Start && timeOfDay < w.End);
    }

    private static string Serialize(IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>> schedule)
    {
        var byIntKey = schedule.ToDictionary(kv => (int)kv.Key, kv => kv.Value);
        return JsonSerializer.Serialize(byIntKey);
    }

    private static Dictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var byIntKey = JsonSerializer.Deserialize<Dictionary<int, List<BusinessHoursWindow>>>(json);
            return byIntKey?.ToDictionary(kv => (DayOfWeek)kv.Key, kv => (IReadOnlyList<BusinessHoursWindow>)kv.Value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
