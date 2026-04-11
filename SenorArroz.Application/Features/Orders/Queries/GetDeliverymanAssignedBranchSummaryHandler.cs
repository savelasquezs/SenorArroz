using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Enums;

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

    /// <summary>
    /// Instante en que el pedido entró en ruta: <c>status_times.ontheway</c> (como guarda el dominio),
    /// o <c>on_the_way</c> si existiera legado; si no hay marca, <c>created_at</c>.
    /// </summary>
    private const string RouteInstantSql =
        "CASE WHEN o.status_times ? 'ontheway' THEN (o.status_times->>'ontheway')::timestamptz "
        + "WHEN o.status_times ? 'on_the_way' THEN (o.status_times->>'on_the_way')::timestamptz "
        + "ELSE o.created_at END";

    private sealed class BranchAggRow
    {
        public int BranchId { get; set; }
        public int OrderCount { get; set; }
        public decimal TotalDeliveryFee { get; set; }
    }

    private static string BuildStatusPredicateSql(GetDeliverymanAssignedBranchSummaryQuery request)
    {
        if (request.IncludeOnsiteActiveInHistory && request.Status == OrderStatus.Delivered)
        {
            return "(o.status = 'delivered' OR (o.type = 'onsite' AND o.status = 'on_the_way'))";
        }

        if (request.Status.HasValue)
            return $"o.status = '{OrderStatusToColumn(request.Status.Value)}'";

        return "TRUE";
    }

    private static string OrderStatusToColumn(OrderStatus status) => status switch
    {
        OrderStatus.Taken => "taken",
        OrderStatus.InPreparation => "in_preparation",
        OrderStatus.Ready => "ready",
        OrderStatus.OnTheWay => "on_the_way",
        OrderStatus.Delivered => "delivered",
        OrderStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

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

        var useRouteInstantForDates = fromUtc.HasValue && toUtc.HasValue;

        List<(int BranchId, int OrderCount, decimal TotalDeliveryFee)> grouped;

        if (useRouteInstantForDates)
        {
            var statusPred = BuildStatusPredicateSql(request);
            var sql =
                """
                SELECT o.branch_id AS "BranchId",
                       COUNT(*)::int AS "OrderCount",
                       COALESCE(SUM(COALESCE(o.delivery_fee, 0)), 0)::decimal AS "TotalDeliveryFee"
                FROM "order" o
                WHERE o.delivery_man_id = {0}
                  AND (
                """
                + statusPred
                + """
                  )
                  AND (
                """
                + RouteInstantSql
                + """
                  ) >= {1}
                  AND (
                """
                + RouteInstantSql
                + """
                  ) <= {2}
                GROUP BY o.branch_id
                """;

            var rows = await _context.Database
                .SqlQueryRaw<BranchAggRow>(sql, request.DeliveryManId, fromUtc!.Value, toUtc!.Value)
                .ToListAsync(cancellationToken);

            grouped = rows.Select(r => (r.BranchId, r.OrderCount, r.TotalDeliveryFee)).ToList();
        }
        else
        {
            var query = _context.Orders.AsNoTracking()
                .Where(o => o.DeliveryManId == request.DeliveryManId);

            if (request.IncludeOnsiteActiveInHistory && request.Status == OrderStatus.Delivered)
            {
                query = query.Where(o =>
                    o.Status == OrderStatus.Delivered
                    || (o.Type == OrderType.Onsite && o.Status == OrderStatus.OnTheWay));
            }
            else if (request.Status.HasValue)
                query = query.Where(o => o.Status == request.Status.Value);

            if (fromUtc.HasValue)
                query = query.Where(o => o.CreatedAt >= fromUtc.Value);
            if (toUtc.HasValue)
                query = query.Where(o => o.CreatedAt <= toUtc.Value);

            var linqGrouped = await query
                .GroupBy(o => o.BranchId)
                .Select(g => new
                {
                    BranchId = g.Key,
                    OrderCount = g.Count(),
                    TotalDeliveryFee = g.Sum(o => (decimal)(o.DeliveryFee ?? 0)),
                })
                .ToListAsync(cancellationToken);

            grouped = linqGrouped
                .Select(x => (x.BranchId, x.OrderCount, x.TotalDeliveryFee))
                .ToList();
        }

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
