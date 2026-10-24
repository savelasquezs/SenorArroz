using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Models;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Application.Features.Dashboard.Services;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardSalesEvolutionHandler
    : IRequestHandler<GetDashboardSalesEvolutionQuery, DashboardSalesEvolutionResponseDto>
{
    private const int MaxRangeDays = 400;

    private readonly IOrderRepository _orderRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IApplicationDbContext _db;

    public GetDashboardSalesEvolutionHandler(
        IOrderRepository orderRepository,
        IBranchRepository branchRepository,
        ICurrentUser currentUser,
        IApplicationDbContext db)
    {
        _orderRepository = orderRepository;
        _branchRepository = branchRepository;
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<DashboardSalesEvolutionResponseDto> Handle(
        GetDashboardSalesEvolutionQuery request,
        CancellationToken cancellationToken)
    {
        var (from, to) = ColombiaTimeHelper.NormalizeDashboardRangeUtc(request.FromUtc, request.ToUtc, MaxRangeDays);
        var branchFilter = ResolveBranchFilter(request.BranchId);

        var (hourDayStart, hourDayEnd) = ColombiaTimeHelper.GetLastColombiaDayBoundsInRangeUtc(from, to);

        var allBranches = (await _branchRepository.GetAllAsync(cancellationToken)).OrderBy(b => b.Name).ToList();
        var branchesInOrder = (branchFilter.HasValue
                ? allBranches.Where(b => b.Id == branchFilter.Value)
                : allBranches)
            .Select(b => (b.Id, b.Name))
            .ToList();

        // A single ordered-row projection powers all eight bucket families (8 reads -> 1).
        // Tenant RLS and EF global filters apply to this IQueryable.
        var dayOfWeek = NormalizeDayOfWeek(request.DayOfWeek);
        var rows = await _db.Orders.AsNoTracking()
            .Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.AwaitingPayment
                && (o.PrepareAt ?? o.CreatedAt) >= from
                && (o.PrepareAt ?? o.CreatedAt) <= to)
            .Where(o => !branchFilter.HasValue || o.BranchId == branchFilter.Value)
            .Select(o => new { o.BranchId, o.Total, o.CreatedAt, o.PrepareAt })
            .ToListAsync(cancellationToken);

        var source = rows.Select(o => new
        {
            o.BranchId,
            o.Total,
            EffectiveAt = o.PrepareAt ?? o.CreatedAt,
            Day = ColombiaTimeHelper.OrderSalesEffectiveColombiaCalendarDate(o.CreatedAt, o.PrepareAt),
            Hour = ColombiaTimeHelper.OrderSalesEffectiveColombiaHour(o.CreatedAt, o.PrepareAt),
        }).Where(o => !dayOfWeek.HasValue
            || (o.Day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)o.Day.DayOfWeek) == dayOfWeek.Value)
            .ToList();

        var salesByDay = source.GroupBy(o => new { o.BranchId, o.Day })
            .Select(g => new SalesDayPoint(g.Key.BranchId, g.Key.Day, g.Sum(o => o.Total))).ToList();
        var ordersByDay = source.GroupBy(o => o.Day)
            .Select(g => new OrdersDayPoint(g.Key, g.Count())).ToList();
        var salesByMonth = source.GroupBy(o => new { o.BranchId, o.Day.Year, o.Day.Month })
            .Select(g => new SalesMonthPoint(g.Key.BranchId, g.Key.Year, g.Key.Month, g.Sum(o => o.Total))).ToList();
        var ordersByMonth = source.GroupBy(o => new { o.Day.Year, o.Day.Month })
            .Select(g => new OrdersMonthPoint(g.Key.Year, g.Key.Month, g.Count())).ToList();
        var salesByYear = source.GroupBy(o => new { o.BranchId, o.Day.Year })
            .Select(g => new SalesYearPoint(g.Key.BranchId, g.Key.Year, g.Sum(o => o.Total))).ToList();
        var ordersByYear = source.GroupBy(o => o.Day.Year)
            .Select(g => new OrdersYearPoint(g.Key, g.Count())).ToList();

        // Hourly charts cover only the last Colombian calendar day in the requested range.
        var hours = source.Where(o => o.EffectiveAt >= hourDayStart && o.EffectiveAt <= hourDayEnd).ToList();
        var salesByHour = hours.GroupBy(o => new { o.BranchId, o.Hour })
            .Select(g => new SalesHourPoint(g.Key.BranchId, g.Key.Hour, g.Sum(o => o.Total))).ToList();
        var ordersByHour = hours.GroupBy(o => o.Hour)
            .Select(g => new OrdersHourPoint(g.Key, g.Count())).ToList();

        return SalesDashboardChartBuilder.BuildEvolution(
            from,
            to,
            branchesInOrder,
            salesByDay,
            ordersByDay,
            salesByMonth,
            ordersByMonth,
            salesByYear,
            ordersByYear,
            salesByHour,
            ordersByHour);
    }

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
            return requestedBranchId;
        if (_currentUser.BranchId <= 0)
            throw new UnauthorizedAccessException("Usuario sin sucursal asignada.");
        return _currentUser.BranchId;
    }

    private static int? NormalizeDayOfWeek(int? dayOfWeek)
    {
        if (!dayOfWeek.HasValue || dayOfWeek.Value < 1 || dayOfWeek.Value > 7)
            return null;
        return dayOfWeek.Value;
    }
}
