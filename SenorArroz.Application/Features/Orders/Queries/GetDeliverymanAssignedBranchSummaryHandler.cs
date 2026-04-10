using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Options;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetDeliverymanAssignedBranchSummaryHandler
    : IRequestHandler<GetDeliverymanAssignedBranchSummaryQuery, List<DeliverymanAssignedBranchSummaryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly decimal _deliveryFeePayRate;

    public GetDeliverymanAssignedBranchSummaryHandler(
        IApplicationDbContext context,
        IOptions<DeliveryPayrollOptions> payrollOptions)
    {
        _context = context;
        _deliveryFeePayRate = ClampPayRate(payrollOptions.Value.DeliveryFeePayRate);
    }

    private static decimal ClampPayRate(decimal rate)
    {
        if (rate < 0) return 0;
        if (rate > 1) return 1;
        return rate;
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
            .Select(g => new
            {
                BranchId = g.Key,
                OrderCount = g.Count(),
                TotalDeliveryFee = g.Sum(o => (decimal)(o.DeliveryFee ?? 0))
            })
            .ToListAsync(cancellationToken);

        if (grouped.Count == 0)
            return new List<DeliverymanAssignedBranchSummaryDto>();

        var branchIds = grouped.Select(x => x.BranchId).ToList();
        var names = await _context.Branches.AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        return grouped
            .Select(x =>
            {
                var payable = Math.Round(x.TotalDeliveryFee * _deliveryFeePayRate, 2);
                return new DeliverymanAssignedBranchSummaryDto
                {
                    BranchId = x.BranchId,
                    BranchName = names.TryGetValue(x.BranchId, out var n) ? n : $"Sucursal #{x.BranchId}",
                    OrderCount = x.OrderCount,
                    DeliveredCount = x.OrderCount,
                    TotalDeliveryFee = x.TotalDeliveryFee,
                    PayableDeliveryFee = payable,
                };
            })
            .OrderBy(x => x.BranchName)
            .ToList();
    }
}
