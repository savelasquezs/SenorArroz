using MediatR;
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

    public GetDashboardSalesEvolutionHandler(
        IOrderRepository orderRepository,
        IBranchRepository branchRepository,
        ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _branchRepository = branchRepository;
        _currentUser = currentUser;
    }

    public async Task<DashboardSalesEvolutionResponseDto> Handle(
        GetDashboardSalesEvolutionQuery request,
        CancellationToken cancellationToken)
    {
        var (from, to) = NormalizeRange(request.FromUtc, request.ToUtc);
        var branchFilter = ResolveBranchFilter(request.BranchId);

        var hourDayStart = new DateTime(to.Year, to.Month, to.Day, 0, 0, 0, DateTimeKind.Utc);
        var hourDayEnd = hourDayStart.AddDays(1).AddTicks(-1);

        var allBranches = (await _branchRepository.GetAllAsync()).OrderBy(b => b.Name).ToList();
        var branchesInOrder = (branchFilter.HasValue
                ? allBranches.Where(b => b.Id == branchFilter.Value)
                : allBranches)
            .Select(b => (b.Id, b.Name))
            .ToList();

        // Secuencial: un mismo DbContext (scoped) no admite varias consultas activas en paralelo.
        var salesByDay = await _orderRepository.GetDashboardSalesByDayAsync(branchFilter, from, to, cancellationToken);
        var ordersByDay = await _orderRepository.GetDashboardOrdersByDayAsync(branchFilter, from, to, cancellationToken);
        var salesByMonth = await _orderRepository.GetDashboardSalesByMonthAsync(branchFilter, from, to, cancellationToken);
        var ordersByMonth = await _orderRepository.GetDashboardOrdersByMonthAsync(branchFilter, from, to, cancellationToken);
        var salesByYear = await _orderRepository.GetDashboardSalesByYearAsync(branchFilter, from, to, cancellationToken);
        var ordersByYear = await _orderRepository.GetDashboardOrdersByYearAsync(branchFilter, from, to, cancellationToken);
        var salesByHour = await _orderRepository.GetDashboardSalesByHourAsync(
            branchFilter,
            hourDayStart,
            hourDayEnd,
            cancellationToken);
        var ordersByHour = await _orderRepository.GetDashboardOrdersByHourAsync(
            branchFilter,
            hourDayStart,
            hourDayEnd,
            cancellationToken);

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

    private static (DateTime From, DateTime To) NormalizeRange(DateTime fromUtc, DateTime toUtc)
    {
        var from = fromUtc;
        var to = toUtc;
        if (to < from)
            (from, to) = (to, from);

        if ((to.Date - from.Date).TotalDays + 1 > MaxRangeDays)
            to = from.Date.AddDays(MaxRangeDays - 1).AddDays(1).AddTicks(-1);

        return (from, to);
    }

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (_currentUser.Role == "superadmin")
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }
}
