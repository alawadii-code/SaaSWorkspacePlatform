using Microsoft.EntityFrameworkCore;

namespace Workspace.Application.Common;

/// <summary>
/// Validated paging request. Bounds are enforced here rather than trusted from
/// the query string, so a client cannot ask for a million rows.
/// </summary>
public sealed record PageRequest
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public PageRequest(int? page = null, int? pageSize = null)
    {
        Page = page is null or < 1 ? 1 : page.Value;
        PageSize = pageSize switch
        {
            null or < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize.Value,
        };
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Skip => (Page - 1) * PageSize;
}

/// <summary>One page of results plus the totals a client needs to paginate.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;
}

public static class QueryableExtensions
{
    /// <summary>
    /// Runs a count and a page query for <paramref name="source"/>, mapping
    /// rows with <paramref name="map"/>.
    /// </summary>
    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TSource, TResult>(
        this IQueryable<TSource> source,
        PageRequest page,
        Func<TSource, TResult> map,
        CancellationToken cancellationToken = default)
    {
        var total = await source.CountAsync(cancellationToken);

        var items = await source
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(
            [.. items.Select(map)],
            page.Page,
            page.PageSize,
            total);
    }
}
