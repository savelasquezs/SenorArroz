using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Queries;

public class GetDeliverymanHistoryNeighborhoodsHandler
    : IRequestHandler<GetDeliverymanHistoryNeighborhoodsQuery, List<DeliverymanHistoryNeighborhoodDto>>
{
    private readonly IApplicationDbContext _context;

    public GetDeliverymanHistoryNeighborhoodsHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<DeliverymanHistoryNeighborhoodDto>> Handle(
        GetDeliverymanHistoryNeighborhoodsQuery request,
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

        if (request.BranchId is > 0)
            query = query.Where(o => o.BranchId == request.BranchId!.Value);

        if (fromUtc.HasValue)
            query = query.Where(o => o.CreatedAt >= fromUtc.Value);
        if (toUtc.HasValue)
            query = query.Where(o => o.CreatedAt <= toUtc.Value);

        var neighborhoodIds = await query
            .Where(o => o.AddressId != null)
            .Join(
                _context.Addresses.AsNoTracking(),
                o => o.AddressId!.Value,
                a => a.Id,
                (_, a) => a.NeighborhoodId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (neighborhoodIds.Count == 0)
            return new List<DeliverymanHistoryNeighborhoodDto>();

        return await _context.Neighborhoods.AsNoTracking()
            .Where(n => neighborhoodIds.Contains(n.Id))
            .OrderBy(n => n.Name)
            .Select(n => new DeliverymanHistoryNeighborhoodDto { Id = n.Id, Name = n.Name })
            .ToListAsync(cancellationToken);
    }
}
