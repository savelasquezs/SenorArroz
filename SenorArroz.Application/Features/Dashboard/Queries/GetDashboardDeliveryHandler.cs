using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Application.Features.Dashboard.Services;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardDeliveryHandler : IRequestHandler<GetDashboardDeliveryQuery, DashboardDeliveryResponseDto>
{
    private const int MaxRangeDays = 400;

    private readonly IOrderRepository _orderRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUser _currentUser;

    public GetDashboardDeliveryHandler(
        IOrderRepository orderRepository,
        IUserRepository userRepository,
        ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _userRepository = userRepository;
        _currentUser = currentUser;
    }

    public async Task<DashboardDeliveryResponseDto> Handle(
        GetDashboardDeliveryQuery request,
        CancellationToken cancellationToken)
    {
        var (from, to) = ClampRange(request.FromUtc, request.ToUtc);

        var branchFilter = ResolveBranchFilter(request.BranchId);

        var deliveryManId = request.DeliveryManId;
        if (deliveryManId.HasValue)
            await AssertDeliveryManInScopeAsync(deliveryManId.Value, branchFilter, cancellationToken);

        var orders = await _orderRepository.GetDeliveredDeliveryOrdersForDashboardAsync(
            branchFilter,
            from,
            to,
            deliveryManId,
            cancellationToken);

        var salesTicks = await _orderRepository.GetDeliveredOrdersSalesTicksForDashboardAsync(
            branchFilter,
            from,
            to,
            deliveryManId,
            cancellationToken);

        var agg = DeliveryDashboardAggregator.Build(orders, salesTicks, from, to);

        return new DashboardDeliveryResponseDto
        {
            AvgPrepMinutes = agg.AvgPrepMinutes,
            AvgDeliveryMinutes = agg.AvgDeliveryMinutes,
            Deliverymen = agg.Deliverymen.Select(d => new DeliverymanEfficiencyApiDto
            {
                Id = d.Id,
                BranchId = d.BranchId,
                Name = d.Name,
                DeliveredCount = d.DeliveredCount,
                AvgDeliveryMinutes = d.AvgDeliveryMinutes,
                DeliveryFeeTotal = d.DeliveryFeeTotal,
            }).ToList(),
            EvolutionLabels = agg.EvolutionLabels,
            EvolutionDeliveries = agg.EvolutionDeliveries,
            EvolutionFees = agg.EvolutionFees,
            EvolutionSalesTotals = agg.EvolutionSalesTotals,
            PeriodFeeToSalesPercent = agg.PeriodFeeToSalesPercent,
        };
    }

    private static (DateTime From, DateTime To) ClampRange(DateTime fromUtc, DateTime toUtc)
    {
        var from = fromUtc;
        var to = toUtc;
        if (to < from)
            (from, to) = (to, from);

        var spanDays = (to.Date - from.Date).TotalDays + 1;
        if (spanDays > MaxRangeDays)
            to = from.Date.AddDays(MaxRangeDays - 1).AddDays(1).AddTicks(-1);

        return (from, to);
    }

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (_currentUser.Role == "superadmin")
            return requestedBranchId;
        if (string.Equals(_currentUser.Role, "deliveryman", StringComparison.OrdinalIgnoreCase))
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }

    private async Task AssertDeliveryManInScopeAsync(
        int deliveryManId,
        int? branchFilter,
        CancellationToken cancellationToken)
    {
        if (string.Equals(_currentUser.Role, "deliveryman", StringComparison.OrdinalIgnoreCase))
        {
            if (deliveryManId != _currentUser.Id)
                throw new UnauthorizedAccessException("No puedes consultar métricas de otro domiciliario.");
            return;
        }

        var user = await _userRepository.GetByIdAsync(deliveryManId, cancellationToken);
        if (user is not { Active: true } || user.Role != UserRole.Deliveryman)
            throw new UnauthorizedAccessException("Domiciliario no encontrado o inactivo.");

        if (_currentUser.Role == "admin")
        {
            if (user.BranchId != _currentUser.BranchId)
                throw new UnauthorizedAccessException("Domiciliario no pertenece a tu sucursal.");
            return;
        }

        if (_currentUser.Role == "superadmin")
        {
            if (branchFilter.HasValue && user.BranchId != branchFilter.Value)
                throw new UnauthorizedAccessException("Domiciliario no pertenece a la sucursal seleccionada.");
        }
    }
}
