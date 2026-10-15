using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Orders.Queries;

/// <summary>
/// Avoid scanning all historical orders and N full-order requests just to
/// calculate kitchen product/category totals for one Colombia calendar day.
/// </summary>
public sealed class GetKitchenSummaryHandler(
    IApplicationDbContext db, ICurrentUser currentUser, IMapper mapper)
    : IRequestHandler<GetKitchenSummaryQuery, List<KitchenSummaryOrderDto>>
{
    public async Task<List<KitchenSummaryOrderDto>> Handle(
        GetKitchenSummaryQuery request, CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(
            request.ColombiaDate.Date, request.ColombiaDate.Date);
        var orders = db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Taken || o.Status == OrderStatus.InPreparation)
            // Keep exactly the same business date used by the existing frontend summary:
            // PrepareAt when present, otherwise CreatedAt (not ReservedFor).
            .Where(o => (o.PrepareAt ?? o.CreatedAt) >= fromUtc
                && (o.PrepareAt ?? o.CreatedAt) <= toUtc);

        var branchId = Roles.IsSuperadmin(currentUser.Role) ? request.BranchId : currentUser.BranchId;
        if (branchId.HasValue)
            orders = orders.Where(o => o.BranchId == branchId.Value);

        var page = await orders
            .OrderBy(o => o.Id)
            .Select(o => new KitchenSummaryOrderDto
            {
                Id = o.Id, Status = o.Status,
                PrepareAt = o.PrepareAt, CreatedAt = o.CreatedAt
            })
            .ToListAsync(cancellationToken);
        if (page.Count == 0) return page;

        var ids = page.Select(o => o.Id).ToArray();
        var details = await db.OrderDetails.AsNoTracking()
            .Where(d => ids.Contains(d.OrderId))
            .Include(d => d.Product).ThenInclude(p => p.Category)
            .OrderBy(d => d.OrderId).ThenBy(d => d.Id)
            .ToListAsync(cancellationToken);
        var grouped = details.GroupBy(d => d.OrderId)
            .ToDictionary(g => g.Key, g => mapper.Map<List<OrderDetailDto>>(g.ToList()));

        foreach (var order in page)
            order.KitchenDetails = grouped.GetValueOrDefault(order.Id) ?? [];
        return page;
    }
}
