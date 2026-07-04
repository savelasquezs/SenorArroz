using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

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
        var (from, to) = ColombiaTimeHelper.NormalizeDashboardRangeUtc(request.FromUtc, request.ToUtc, MaxRangeDays);

        var top = Math.Clamp(request.Top <= 0 ? 10 : request.Top, 5, 20);
        var branchFilter = ResolveBranchFilter(request.BranchId);
        var dayOfWeek = NormalizeDayOfWeek(request.DayOfWeek);

        List<RankRow> rows;
        if (request.GroupBy == SalesProductsGroupBy.Category)
        {
            var agg = await _orderRepository.GetSalesCategoryAggregatesForDashboardAsync(
                branchFilter,
                from,
                to,
                dayOfWeek,
                cancellationToken);
            rows = agg
                .Select(r => new RankRow(
                    r.CategoryId,
                    string.IsNullOrWhiteSpace(r.CategoryName) ? $"#{r.CategoryId}" : r.CategoryName,
                    r.QuantitySold,
                    r.RevenueCop))
                .ToList();
        }
        else
        {
            var agg = await _orderRepository.GetSalesProductAggregatesForDashboardAsync(
                branchFilter,
                from,
                to,
                dayOfWeek,
                cancellationToken);
            rows = agg
                .Select(r => new RankRow(
                    r.ProductId,
                    string.IsNullOrWhiteSpace(r.ProductName) ? $"#{r.ProductId}" : r.ProductName,
                    r.QuantitySold,
                    r.RevenueCop))
                .ToList();
        }

        var totalRevenue = rows.Sum(r => r.RevenueCop);
        var totalQty = rows.Sum(r => r.QuantitySold);

        // Peso por categoría (productos con weight_grams). Futuro: cruzar con gastos/insumos por categoría — no implementado.
        var weightRows = await _orderRepository.GetSalesCategoryWeightAggregatesForDashboardAsync(
            branchFilter,
            from,
            to,
            dayOfWeek,
            cancellationToken);

        var topByQuantity = rows
            .OrderByDescending(r => r.QuantitySold)
            .ThenBy(r => r.Name)
            .Take(top)
            .Select(r => new SalesRankItemDto
            {
                Id = r.Id,
                Name = r.Name,
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
            WeightByCategory = weightRows
                .Select(w => new SalesCategoryWeightItemDto
                {
                    CategoryId = w.CategoryId,
                    Name = w.CategoryName,
                    TotalWeightGrams = w.TotalWeightGrams,
                })
                .ToList(),
        };
    }

    private static List<RevenueParticipationSliceDto> BuildParticipationSlices(
        IReadOnlyList<RankRow> rows,
        long totalRevenue)
    {
        var list = new List<RevenueParticipationSliceDto>();
        if (totalRevenue <= 0 || rows.Count == 0)
            return list;

        var topByRev = rows
            .OrderByDescending(r => r.RevenueCop)
            .ThenBy(r => r.Name)
            .Take(ParticipationTop)
            .ToList();

        var topSum = topByRev.Sum(r => r.RevenueCop);
        var others = Math.Max(0L, totalRevenue - topSum);

        foreach (var r in topByRev)
        {
            list.Add(new RevenueParticipationSliceDto
            {
                Label = r.Name,
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
        if (Roles.IsSuperadmin(_currentUser.Role))
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }

    private static int? NormalizeDayOfWeek(int? dayOfWeek)
    {
        if (!dayOfWeek.HasValue || dayOfWeek.Value < 1 || dayOfWeek.Value > 7)
            return null;
        return dayOfWeek.Value;
    }

    private sealed record RankRow(int Id, string Name, int QuantitySold, long RevenueCop);
}
