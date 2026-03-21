using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Models;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardSalesProductsHandler
    : IRequestHandler<GetDashboardSalesProductsQuery, DashboardSalesProductsResponseDto>
{
    private const int MaxRangeDays = 400;
    private const int ParticipationTop = 5;

    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUser _currentUser;

    public GetDashboardSalesProductsHandler(IOrderRepository orderRepository, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public async Task<DashboardSalesProductsResponseDto> Handle(
        GetDashboardSalesProductsQuery request,
        CancellationToken cancellationToken)
    {
        var from = request.FromUtc;
        var to = request.ToUtc;
        if (to < from)
            (from, to) = (to, from);

        if ((to.Date - from.Date).TotalDays + 1 > MaxRangeDays)
            to = from.Date.AddDays(MaxRangeDays - 1).AddDays(1).AddTicks(-1);

        var top = Math.Clamp(request.Top <= 0 ? 10 : request.Top, 5, 20);
        var branchFilter = ResolveBranchFilter(request.BranchId);

        var rows = await _orderRepository.GetSalesProductAggregatesForDashboardAsync(
            branchFilter,
            from,
            to,
            cancellationToken);

        var totalRevenue = rows.Sum(r => r.RevenueCop);
        var totalQty = rows.Sum(r => r.QuantitySold);

        var topByQuantity = rows
            .OrderByDescending(r => r.QuantitySold)
            .ThenBy(r => r.ProductName)
            .Take(top)
            .Select(r => new SalesProductRankDto
            {
                ProductId = r.ProductId,
                Name = string.IsNullOrWhiteSpace(r.ProductName) ? $"#{r.ProductId}" : r.ProductName,
                QuantitySold = r.QuantitySold,
                RevenueCop = r.RevenueCop,
            })
            .ToList();

        var participation = BuildParticipationSlices(rows, totalRevenue);

        return new DashboardSalesProductsResponseDto
        {
            TopByQuantity = topByQuantity,
            ParticipationByRevenue = participation,
            TotalRevenueCop = totalRevenue,
            TotalQuantity = totalQty,
        };
    }

    private static List<RevenueParticipationSliceDto> BuildParticipationSlices(
        IReadOnlyList<SalesProductAggregateRow> rows,
        long totalRevenue)
    {
        var list = new List<RevenueParticipationSliceDto>();
        if (totalRevenue <= 0 || rows.Count == 0)
            return list;

        var topByRev = rows
            .OrderByDescending(r => r.RevenueCop)
            .ThenBy(r => r.ProductName)
            .Take(ParticipationTop)
            .ToList();

        var topSum = topByRev.Sum(r => r.RevenueCop);
        var others = Math.Max(0L, totalRevenue - topSum);

        foreach (var r in topByRev)
        {
            var label = string.IsNullOrWhiteSpace(r.ProductName) ? $"#{r.ProductId}" : r.ProductName;
            list.Add(new RevenueParticipationSliceDto
            {
                Label = label,
                RevenueCop = r.RevenueCop,
                Percent = Math.Round(100.0 * r.RevenueCop / totalRevenue, 1),
            });
        }

        if (others > 0)
        {
            list.Add(new RevenueParticipationSliceDto
            {
                Label = "Otros",
                RevenueCop = others,
                Percent = Math.Round(100.0 * others / totalRevenue, 1),
            });
        }

        return list;
    }

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (_currentUser.Role == "superadmin")
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }
}
