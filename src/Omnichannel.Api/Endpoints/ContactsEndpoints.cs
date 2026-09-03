using Microsoft.AspNetCore.Mvc;
using Omnichannel.Api.Validation;
using Omnichannel.Application.Contacts;
using Omnichannel.Contracts.Conversations;
using Omnichannel.Domain.Authorization;

namespace Omnichannel.Api.Endpoints;

public static class ContactsEndpoints
{
    public static IEndpointRouteBuilder MapContactsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/contacts");

        group.MapGet("/", ListAsync).RequireAuthorization(PermissionKeys.ConversationsRead);
        group.MapGet("/{id:guid}", GetByIdAsync).RequireAuthorization(PermissionKeys.ConversationsRead);
        group.MapPost("/", CreateAsync).RequireAuthorization(PermissionKeys.ConversationsReply);

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize,
        ContactService contacts, CancellationToken cancellationToken)
    {
        var result = await contacts.ListAsync(search, page ?? 1, pageSize ?? 20, cancellationToken);
        var items = result.Items.Select(c => new ContactResponse(c.Id, c.DisplayName, c.CreatedAt, c.LastInteractionAt)).ToList();
        return Results.Ok(new PagedResponse<ContactResponse>(items, result.TotalCount, result.Page, result.PageSize));
    }

    private static async Task<IResult> GetByIdAsync(Guid id, ContactService contacts, CancellationToken cancellationToken)
    {
        var contact = await contacts.GetByIdAsync(id, cancellationToken);
        return contact is null
            ? Results.NotFound()
            : Results.Ok(new ContactResponse(contact.Id, contact.DisplayName, contact.CreatedAt, contact.LastInteractionAt));
    }

    private static async Task<IResult> CreateAsync(CreateContactRequest request, ContactService contacts, CancellationToken cancellationToken)
    {
        if (!request.TryValidate(out var errors))
        {
            return Results.ValidationProblem(errors.ToDictionary(_ => "request", e => new[] { e }));
        }

        var contact = await contacts.CreateAsync(request.DisplayName, cancellationToken);
        return Results.Created($"/api/v1/contacts/{contact.Id}", new ContactResponse(contact.Id, contact.DisplayName, contact.CreatedAt, contact.LastInteractionAt));
    }
}
