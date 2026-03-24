using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetDeliverymanAssignedBranchSummaryHandler
    : IRequestHandler<GetDeliverymanAssignedBranchSummaryQuery, List<DeliverymanAssignedBranchSummaryDto>>
{
    private readonly IApplicationDbContext _context;

    public GetDeliverymanAssignedBranchSummaryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<DeliverymanAssignedBranchSummaryDto>> Handle(
        GetDeliverymanAssignedBranchSummaryQuery request,
        CancellationToken cancellationToken)
    {
        DateTime? fromUtc = null;
        DateTime? toUtc = null;
        if (request.FromDate.HasValue || request.ToDate.HasValue)
        {
            var fromCal = (request.FromDate ?? request.ToDate)!.Value.Date;
            var toCal = (request.ToDate ?? request.FromDate)!.Value.Date;
            (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(fromCal, toCal);
        }

        var query = _context.Orders.AsNoTracking()
            .Where(o => o.DeliveryManId == request.DeliveryManId);

        if (request.Status.HasValue)
            query = query.Where(o => o.Status == request.Status.Value);

        if (fromUtc.HasValue)
            query = query.Where(o => o.CreatedAt >= fromUtc.Value);
        if (toUtc.HasValue)
            query = query.Where(o => o.CreatedAt <= toUtc.Value);

        var grouped = await query
            .GroupBy(o => o.BranchId)
            .Select(g => new { BranchId = g.Key, OrderCount = g.Count() })
            .ToListAsync(cancellationToken);

        if (grouped.Count == 0)
            return new List<DeliverymanAssignedBranchSummaryDto>();

        var branchIds = grouped.Select(x => x.BranchId).ToList();
        var names = await _context.Branches.AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        return grouped
            .Select(x => new DeliverymanAssignedBranchSummaryDto
            {
                BranchId = x.BranchId,
                BranchName = names.TryGetValue(x.BranchId, out var n) ? n : $"Sucursal #{x.BranchId}",
                OrderCount = x.OrderCount
            })
            .OrderBy(x => x.BranchName)
            .ToList();
    }
}
