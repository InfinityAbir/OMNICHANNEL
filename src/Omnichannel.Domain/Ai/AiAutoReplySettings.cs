using System.Text.Json;
using Omnichannel.Domain.Common;

namespace Omnichannel.Domain.Ai;

/// <summary>One open time window on a given day, tenant-local time. Same-day only — a window
/// crossing midnight (e.g. 22:00-02:00) is not supported; split it into two windows instead.</summary>
public sealed record BusinessHoursWindow(TimeOnly Start, TimeOnly End);

/// <summary>
/// Per-tenant configuration gating AI auto-reply (PRD §71) — one row per tenant. Disabled by
/// default: auto-reply requires both this tenant-wide switch AND the individual conversation's
/// own <see cref="Conversations.ConversationAiMode"/> to allow it, belt-and-suspenders. Business
/// hours are unconfigured by default, which <see cref="IsWithinBusinessHours"/> treats as "never
/// eligible" rather than assuming 24/7 — the conservative default PRD §71 calls for.
/// </summary>
public sealed class AiAutoReplySettings : ITenantOwned
{
    public Guid TenantId { get; private set; }
    public bool Enabled { get; private set; }
    public double ConfidenceThreshold { get; private set; } = 0.85;
    public int DailyLimit { get; private set; } = 50;
    public string? BusinessHoursJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AiAutoReplySettings()
    {
    }

    public static AiAutoReplySettings CreateDefault(Guid tenantId, DateTimeOffset now)
        => new()
        {
            TenantId = tenantId,
            Enabled = false,
            ConfidenceThreshold = 0.85,
            DailyLimit = 50,
            BusinessHoursJson = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void Configure(
        bool enabled,
        double confidenceThreshold,
        int dailyLimit,
        IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>>? businessHours,
        DateTimeOffset now)
    {
        Enabled = enabled;
        ConfidenceThreshold = Math.Clamp(confidenceThreshold, 0, 1);
        DailyLimit = Math.Max(0, dailyLimit);
        BusinessHoursJson = businessHours is null or { Count: 0 } ? null : Serialize(businessHours);
        UpdatedAt = now;
    }

    public IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindow>> GetBusinessHours()
        => Deserialize(BusinessHoursJson);

    /// <summary>
    /// Evaluated in the tenant's own IANA time zone (<see cref="Tenancy.Tenant.TimeZone"/>), never
    /// server-local time (PRD §71). An unconfigured schedule, or a time zone id that fails to
    /// resolve, is treated as "not open" — the same conservative failure mode as everywhere else
    /// in this feature.
    /// </summary>
    public bool IsWithinBusinessHours(DateTimeOffset nowUtc, string tenantTimeZoneId)
    {
        if (string.IsNullOrWhiteSpace(BusinessHoursJson))
        {
            return false;
        }

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
