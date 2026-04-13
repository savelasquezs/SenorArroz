using MediatR;
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
        var (from, to) = ColombiaTimeHelper.NormalizeDashboardRangeUtc(request.FromUtc, request.ToUtc, MaxRangeDays);
        var branchFilter = ResolveBranchFilter(request.BranchId);

        var (hourDayStart, hourDayEnd) = ColombiaTimeHelper.GetLastColombiaDayBoundsInRangeUtc(from, to);

        var allBranches = (await _branchRepository.GetAllAsync(cancellationToken)).OrderBy(b => b.Name).ToList();
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

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (Roles.IsSuperadmin(_currentUser.Role))
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }
}
