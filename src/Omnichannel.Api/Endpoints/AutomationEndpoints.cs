using Omnichannel.Application.Automation;
using Omnichannel.Contracts.Automation;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Automation;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Api.Endpoints;

public static class AutomationEndpoints
{
    public static IEndpointRouteBuilder MapAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        var rules = app.MapGroup("/api/v1/automation-rules");
        rules.MapGet("/", ListRulesAsync).RequireAuthorization(PermissionKeys.TenantRead);
        rules.MapPost("/", CreateRuleAsync).RequireAuthorization(PermissionKeys.TenantUpdate);
        rules.MapPut("/{id:guid}/enabled", SetRuleEnabledAsync).RequireAuthorization(PermissionKeys.TenantUpdate);
        rules.MapDelete("/{id:guid}", DeleteRuleAsync).RequireAuthorization(PermissionKeys.TenantUpdate);

        var replies = app.MapGroup("/api/v1/saved-replies");
        replies.MapGet("/", ListRepliesAsync).RequireAuthorization(PermissionKeys.ConversationsRead);
        replies.MapPost("/", CreateReplyAsync).RequireAuthorization(PermissionKeys.ConversationsReply);
        replies.MapPut("/{id:guid}", UpdateReplyAsync).RequireAuthorization(PermissionKeys.ConversationsReply);
        replies.MapDelete("/{id:guid}", DeleteReplyAsync).RequireAuthorization(PermissionKeys.ConversationsReply);

        app.MapGet("/api/v1/tenant/business-hours", GetBusinessHoursAsync).RequireAuthorization(PermissionKeys.TenantRead);
        app.MapPut("/api/v1/tenant/business-hours", UpdateBusinessHoursAsync).RequireAuthorization(PermissionKeys.TenantUpdate);

        return app;
    }

    private static async Task<IResult> ListRulesAsync(AutomationRuleService rules, CancellationToken cancellationToken)
    {
        var list = await rules.ListAsync(cancellationToken);
        return Results.Ok(list.Select(ToResponse).ToList());
    }

    private static async Task<IResult> CreateRuleAsync(
        CreateAutomationRuleRequest request, AutomationRuleService rules, CancellationToken cancellationToken)
    {
        ConversationPriority? priority = null;
        if (!string.IsNullOrWhiteSpace(request.SetPriority))
        {
            if (!Enum.TryParse<ConversationPriority>(request.SetPriority, ignoreCase: true, out var parsed))
            {
                return Results.Problem(title: "Invalid priority.", statusCode: StatusCodes.Status400BadRequest);
            }

            priority = parsed;
        }

        if (string.IsNullOrWhiteSpace(request.ApplyTagName) && priority is null && !request.Escalate)
        {
            return Results.Problem(title: "A rule must have at least one action (tag, priority, or escalate).", statusCode: StatusCodes.Status400BadRequest);
        }

        var rule = await rules.CreateAsync(request.Name ?? request.Keyword, request.Keyword, request.ApplyTagName, priority, request.Escalate, cancellationToken);
        return Results.Created($"/api/v1/automation-rules/{rule.Id}", ToResponse(rule));
    }

    private static async Task<IResult> SetRuleEnabledAsync(
        Guid id, SetAutomationRuleEnabledRequest request, AutomationRuleService rules, CancellationToken cancellationToken)
        => await rules.SetEnabledAsync(id, request.Enabled, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> DeleteRuleAsync(Guid id, AutomationRuleService rules, CancellationToken cancellationToken)
        => await rules.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> ListRepliesAsync(SavedReplyService replies, CancellationToken cancellationToken)
    {
        var list = await replies.ListAsync(cancellationToken);
        return Results.Ok(list.Select(r => new SavedReplyResponse(r.Id, r.Title, r.Text, r.CreatedAt, r.UpdatedAt)).ToList());
    }

    private static async Task<IResult> CreateReplyAsync(SavedReplyRequest request, SavedReplyService replies, CancellationToken cancellationToken)
    {
        var reply = await replies.CreateAsync(request.Title, request.Text, cancellationToken);
        return Results.Created($"/api/v1/saved-replies/{reply.Id}", new SavedReplyResponse(reply.Id, reply.Title, reply.Text, reply.CreatedAt, reply.UpdatedAt));
    }

    private static async Task<IResult> UpdateReplyAsync(
        Guid id, SavedReplyRequest request, SavedReplyService replies, CancellationToken cancellationToken)
    {
        var reply = await replies.UpdateAsync(id, request.Title, request.Text, cancellationToken);
        return reply is null
            ? Results.NotFound()
            : Results.Ok(new SavedReplyResponse(reply.Id, reply.Title, reply.Text, reply.CreatedAt, reply.UpdatedAt));
    }

    private static async Task<IResult> DeleteReplyAsync(Guid id, SavedReplyService replies, CancellationToken cancellationToken)
        => await replies.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> GetBusinessHoursAsync(TenantBusinessHoursService service, CancellationToken cancellationToken)
    {
        var hours = await service.GetAsync(cancellationToken);
        return Results.Ok(ToResponse(hours));
    }

    private static async Task<IResult> UpdateBusinessHoursAsync(
        UpdateTenantBusinessHoursRequest request, TenantBusinessHoursService service, CancellationToken cancellationToken)
    {
        // Hardening (Phase 15): the stored columns are bounded (character varying(4000)), so an
        // oversized payload would otherwise surface as an unhandled Postgres data-length error
        // (a 500) instead of a clean validation failure. Reject it here instead — generous bounds
        // that no legitimate schedule/holiday-list could ever approach.
        if (request.Holidays is { Count: > 366 })
        {
            return Results.Problem(title: "Too many holidays.", detail: "At most 366 holiday dates are allowed.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.BusinessHours is { Count: > 7 } || request.BusinessHours?.Values.Any(w => w.Count > 20) == true)
        {
            return Results.Problem(title: "Business hours payload too large.", statusCode: StatusCodes.Status400BadRequest);
        }

        Dictionary<DayOfWeek, IReadOnlyList<Omnichannel.Domain.Ai.BusinessHoursWindow>>? businessHours = null;
        if (request.BusinessHours is not null)
        {
            businessHours = new Dictionary<DayOfWeek, IReadOnlyList<Omnichannel.Domain.Ai.BusinessHoursWindow>>();
            foreach (var (day, windows) in request.BusinessHours)
            {
                var parsedWindows = new List<Omnichannel.Domain.Ai.BusinessHoursWindow>();
                foreach (var window in windows)
                {
                    if (!TimeOnly.TryParse(window.Start, out var start) || !TimeOnly.TryParse(window.End, out var end) || start >= end)
                    {
                        return Results.Problem(
                            title: "Invalid business hours window.",
                            detail: $"'{window.Start}'-'{window.End}' on {day} is not a valid time window. Use \"HH:mm\", start before end.",
                            statusCode: StatusCodes.Status400BadRequest);
                    }

                    parsedWindows.Add(new Omnichannel.Domain.Ai.BusinessHoursWindow(start, end));
                }

                businessHours[day] = parsedWindows;
            }
        }

        var holidays = new List<DateOnly>();
        if (request.Holidays is not null)
        {
            foreach (var holiday in request.Holidays)
            {
                if (!DateOnly.TryParse(holiday, out var parsed))
                {
                    return Results.Problem(
                        title: "Invalid holiday date.",
                        detail: $"'{holiday}' is not a valid date. Use \"yyyy-MM-dd\".",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                holidays.Add(parsed);
            }
        }

        var updated = await service.UpdateAsync(businessHours, holidays, cancellationToken);
        return Results.Ok(ToResponse(updated));
    }

    private static AutomationRuleResponse ToResponse(AutomationRule rule)
        => new(rule.Id, rule.Name, rule.Enabled, rule.Keyword, rule.ApplyTagName, rule.SetPriority?.ToString(), rule.Escalate, rule.CreatedAt);

    private static TenantBusinessHoursResponse ToResponse(TenantBusinessHours hours)
    {
        var businessHours = hours.GetBusinessHours().ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<BusinessHoursWindowRequest>)kv.Value
                .Select(w => new BusinessHoursWindowRequest(
                    w.Start.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    w.End.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)))
                .ToList());

        var holidays = hours.GetHolidays().Select(d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        return new TenantBusinessHoursResponse(businessHours, holidays);
    }
}
