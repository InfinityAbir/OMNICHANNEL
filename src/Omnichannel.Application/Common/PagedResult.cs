namespace Omnichannel.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public sealed record KeysetResult<T>(IReadOnlyList<T> Items, string? NextCursor);
