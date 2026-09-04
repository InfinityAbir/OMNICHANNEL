using System.ComponentModel.DataAnnotations;

namespace Omnichannel.Contracts.Automation;

public sealed record AutomationRuleResponse(
    Guid Id, string Name, bool Enabled, string Keyword, string? ApplyTagName, string? SetPriority, bool Escalate, DateTimeOffset CreatedAt);

public sealed class CreateAutomationRuleRequest
{
    [MaxLength(200)]
    public string? Name { get; init; }

    [Required, MaxLength(200)]
    public string Keyword { get; init; } = string.Empty;

    [MaxLength(100)]
    public string? ApplyTagName { get; init; }

    public string? SetPriority { get; init; }

    public bool Escalate { get; init; }
}

public sealed class SetAutomationRuleEnabledRequest
{
    public bool Enabled { get; init; }
}

public sealed record SavedReplyResponse(Guid Id, string Title, string Text, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed class SavedReplyRequest
{
    [Required, MaxLength(200)]
    public string Title { get; init; } = string.Empty;

    [Required, MaxLength(4000)]
    public string Text { get; init; } = string.Empty;
}

/// <summary>"HH:mm" 24-hour, same-day only.</summary>
public sealed record BusinessHoursWindowRequest(string Start, string End);

public sealed record TenantBusinessHoursResponse(
    IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindowRequest>> BusinessHours,
    IReadOnlyList<string> Holidays);

public sealed record UpdateTenantBusinessHoursRequest(
    IReadOnlyDictionary<DayOfWeek, IReadOnlyList<BusinessHoursWindowRequest>>? BusinessHours,
    IReadOnlyList<string>? Holidays);
