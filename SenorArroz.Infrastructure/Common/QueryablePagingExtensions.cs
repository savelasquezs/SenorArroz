using Microsoft.EntityFrameworkCore;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Common;

public static class QueryablePagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken ct = default,
        bool skipCountForIncompleteFirstPage = false)
    {
        // The first page's length is the exact count when it is not full.
        // Keep the old count-first behavior for all callers that do not opt in.
        List<T>? items = null;
        if (skipCountForIncompleteFirstPage && page == 1)
            items = await query.Take(pageSize).ToListAsync(ct);

        var total = items is not null && items.Count < pageSize
            ? items.Count
            : await query.CountAsync(ct);
        items ??= await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<T>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }
}
