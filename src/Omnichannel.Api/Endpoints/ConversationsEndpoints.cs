using Microsoft.AspNetCore.Mvc;
using Omnichannel.Api.Validation;
using Omnichannel.Application.Contacts;
using Omnichannel.Application.Conversations;
using Omnichannel.Contracts.Conversations;
using Omnichannel.Domain.Authorization;
using Omnichannel.Domain.Channels;
using Omnichannel.Domain.Conversations;

namespace Omnichannel.Api.Endpoints;

public static class ConversationsEndpoints
{
    public static IEndpointRouteBuilder MapConversationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/conversations");

        group.MapGet("/", ListAsync).RequireAuthorization(PermissionKeys.ConversationsRead);
        group.MapGet("/{id:guid}", GetByIdAsync).RequireAuthorization(PermissionKeys.ConversationsRead);
        group.MapPost("/", CreateAsync).RequireAuthorization(PermissionKeys.ConversationsReply);

        group.MapGet("/{id:guid}/messages", ListMessagesAsync).RequireAuthorization(PermissionKeys.ConversationsRead);
        group.MapPost("/{id:guid}/messages", AddMessageAsync).RequireAuthorization(PermissionKeys.ConversationsReply);

        group.MapPost("/{id:guid}/assign", AssignAsync).RequireAuthorization(PermissionKeys.ConversationsAssign);
        group.MapPost("/{id:guid}/unassign", UnassignAsync).RequireAuthorization(PermissionKeys.ConversationsAssign);

        group.MapPost("/{id:guid}/status", ChangeStatusAsync).RequireAuthorization(PermissionKeys.ConversationsClose);
        group.MapPost("/{id:guid}/priority", SetPriorityAsync).RequireAuthorization(PermissionKeys.ConversationsReply);

        group.MapGet("/{id:guid}/notes", ListNotesAsync).RequireAuthorization(PermissionKeys.ConversationsRead);
        group.MapPost("/{id:guid}/notes", AddNoteAsync).RequireAuthorization(PermissionKeys.ConversationsReply);

        group.MapPost("/{id:guid}/tags", AddTagAsync).RequireAuthorization(PermissionKeys.ConversationsReply);
        group.MapDelete("/{id:guid}/tags/{tagId:guid}", RemoveTagAsync).RequireAuthorization(PermissionKeys.ConversationsReply);

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] string? status, [FromQuery] Guid? assignedUserId, [FromQuery] string? cursor, [FromQuery] int? pageSize,
        ConversationService conversations, CancellationToken cancellationToken)
    {
        ConversationStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ConversationStatus>(status, true, out var parsed))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Unknown status value."] });
            }

            statusFilter = parsed;
        }

        var result = await conversations.ListAsync(statusFilter, assignedUserId, cursor, pageSize ?? 20, cancellationToken);
        var items = result.Items.Select(ToResponse).ToList();
        return Results.Ok(new KeysetPageResponse<ConversationSummaryResponse>(items, result.NextCursor));
    }

    private static async Task<IResult> GetByIdAsync(Guid id, ConversationService conversations, CancellationToken cancellationToken)
    {
        var detail = await conversations.GetDetailAsync(id, cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(ToResponse(detail));
    }

    private static async Task<IResult> CreateAsync(
        CreateConversationRequest request, ConversationService conversations, ContactService contacts, CancellationToken cancellationToken)
    {
        Guid contactId;
        if (request.ContactId is { } existingId)
        {
            var existing = await contacts.GetByIdAsync(existingId, cancellationToken);
            if (existing is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["contactId"] = ["Contact not found."] });
            }

            contactId = existing.Id;
        }
        else if (!string.IsNullOrWhiteSpace(request.NewContactDisplayName))
        {
            var created = await contacts.CreateAsync(request.NewContactDisplayName, cancellationToken);
            contactId = created.Id;
        }
        else
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["contactId"] = ["Either contactId or newContactDisplayName is required."],
            });
        }

        var conversation = await conversations.CreateManualAsync(contactId, request.InitialMessageText, cancellationToken);
        var detail = await conversations.GetDetailAsync(conversation.Id, cancellationToken);
        return Results.Created($"/api/v1/conversations/{conversation.Id}", ToResponse(detail!));
    }

    private static async Task<IResult> ListMessagesAsync(
        Guid id, [FromQuery] string? cursor, [FromQuery] int? pageSize,
        ConversationService conversations, CancellationToken cancellationToken)
    {
        var result = await conversations.ListMessagesAsync(id, cursor, pageSize ?? 50, cancellationToken);
        var items = result.Items.Select(m => new MessageResponse(
            m.Id, m.Direction.ToString(), m.SenderType.ToString(), m.ContentType.ToString(), m.Text, m.CreatedAt, m.DeliveryStatus.ToString())).ToList();
        return Results.Ok(new KeysetPageResponse<MessageResponse>(items, result.NextCursor));
    }

    private static async Task<IResult> AddMessageAsync(
        Guid id, AddMessageRequest request, ConversationService conversations, CancellationToken cancellationToken)
    {
        if (!request.TryValidate(out var errors)
            || !Enum.TryParse<MessageDirection>(request.Direction, true, out var direction)
            || !Enum.TryParse<MessageSenderType>(request.SenderType, true, out var senderType))
        {
            return Results.ValidationProblem(errors.Count > 0
                ? errors.ToDictionary(_ => "request", e => new[] { e })
                : new Dictionary<string, string[]> { ["direction"] = ["Invalid direction or senderType."] });
        }

        var message = await conversations.AddMessageAsync(id, direction, senderType, request.Text, cancellationToken);
        return message is null
            ? Results.NotFound()
            : Results.Ok(new MessageResponse(
                message.Id, message.Direction.ToString(), message.SenderType.ToString(), message.ContentType.ToString(),
                message.Text, message.CreatedAt, message.DeliveryStatus.ToString()));
    }

    private static async Task<IResult> AssignAsync(Guid id, AssignConversationRequest request, ConversationService conversations, CancellationToken cancellationToken)
        => await conversations.AssignAsync(id, request.UserId, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> UnassignAsync(Guid id, ConversationService conversations, CancellationToken cancellationToken)
        => await conversations.UnassignAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> ChangeStatusAsync(Guid id, ChangeStatusRequest request, ConversationService conversations, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ConversationStatus>(request.Status, true, out var status))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Unknown status value."] });
        }

        return await conversations.ChangeStatusAsync(id, status, cancellationToken) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> SetPriorityAsync(Guid id, SetPriorityRequest request, ConversationService conversations, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ConversationPriority>(request.Priority, true, out var priority))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["priority"] = ["Unknown priority value."] });
        }

        return await conversations.SetPriorityAsync(id, priority, cancellationToken) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> ListNotesAsync(Guid id, ConversationService conversations, CancellationToken cancellationToken)
    {
        var notes = await conversations.ListNotesAsync(id, cancellationToken);
        return Results.Ok(notes.Select(n => new NoteResponse(n.Id, n.AuthorUserId, n.Text, n.CreatedAt)).ToList());
    }

    private static async Task<IResult> AddNoteAsync(Guid id, AddNoteRequest request, ConversationService conversations, CancellationToken cancellationToken)
    {
        if (!request.TryValidate(out var errors))
        {
            return Results.ValidationProblem(errors.ToDictionary(_ => "request", e => new[] { e }));
        }

        var note = await conversations.AddNoteAsync(id, request.Text, cancellationToken);
        return note is null
            ? Results.NotFound()
            : Results.Created($"/api/v1/conversations/{id}/notes/{note.Id}", new NoteResponse(note.Id, note.AuthorUserId, note.Text, note.CreatedAt));
    }

    private static async Task<IResult> AddTagAsync(Guid id, AddTagRequest request, ConversationService conversations, CancellationToken cancellationToken)
    {
        if (!request.TryValidate(out var errors))
        {
            return Results.ValidationProblem(errors.ToDictionary(_ => "request", e => new[] { e }));
        }

        return await conversations.AddTagAsync(id, request.Name, cancellationToken) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> RemoveTagAsync(Guid id, Guid tagId, ConversationService conversations, CancellationToken cancellationToken)
        => await conversations.RemoveTagAsync(id, tagId, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static ConversationSummaryResponse ToResponse(ConversationSummary c)
        => new(c.Id, c.ContactId, c.ContactDisplayName, c.ChannelAccountId, c.Status.ToString(), c.Priority.ToString(),
            c.AssignedUserId, c.LastMessageAt, c.Tags);

    private static ConversationDetailResponse ToResponse(ConversationDetail c)
        => new(c.Id, c.ContactId, c.ContactDisplayName, c.ChannelAccountId, c.Status.ToString(), c.Priority.ToString(),
            c.AssignedUserId, c.AiMode.ToString(), c.LastMessageAt, c.CreatedAt, c.ClosedAt, c.Tags);
}
