using Omnichannel.Api.Validation;
using Omnichannel.Application.Conversations;
using Omnichannel.Contracts.Conversations;
using Omnichannel.Domain.Authorization;

namespace Omnichannel.Api.Endpoints;

public static class TagsEndpoints
{
    public static IEndpointRouteBuilder MapTagsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tags");

        group.MapGet("/", ListAsync).RequireAuthorization(PermissionKeys.ConversationsRead);
        group.MapPost("/", CreateAsync).RequireAuthorization(PermissionKeys.ConversationsReply);

        return app;
    }

    private static async Task<IResult> ListAsync(TagService tags, CancellationToken cancellationToken)
    {
        var result = await tags.ListAsync(cancellationToken);
        return Results.Ok(result.Select(t => new TagResponse(t.Id, t.Name)).ToList());
    }

    private static async Task<IResult> CreateAsync(AddTagRequest request, TagService tags, CancellationToken cancellationToken)
    {
        if (!request.TryValidate(out var errors))
        {
            return Results.ValidationProblem(errors.ToDictionary(_ => "request", e => new[] { e }));
        }

        var tag = await tags.CreateAsync(request.Name, cancellationToken);
        return Results.Ok(new TagResponse(tag.Id, tag.Name));
    }
}
