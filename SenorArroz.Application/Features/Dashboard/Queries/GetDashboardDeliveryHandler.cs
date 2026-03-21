using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Application.Features.Dashboard.Services;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardDeliveryHandler : IRequestHandler<GetDashboardDeliveryQuery, DashboardDeliveryResponseDto>
{
    private const int MaxRangeDays = 400;

    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUser _currentUser;

    public GetDashboardDeliveryHandler(IOrderRepository orderRepository, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public async Task<DashboardDeliveryResponseDto> Handle(
        GetDashboardDeliveryQuery request,
        CancellationToken cancellationToken)
    {
        var from = request.FromUtc;
        var to = request.ToUtc;
        if (to < from)
            (from, to) = (to, from);

        var spanDays = (to.Date - from.Date).TotalDays + 1;
        if (spanDays > MaxRangeDays)
        {
            // Acotar al máximo permitido (fin del último día incluido).
            to = from.Date.AddDays(MaxRangeDays - 1).AddDays(1).AddTicks(-1);
        }

        var branchFilter = ResolveBranchFilter(request.BranchId);

        var orders = await _orderRepository.GetDeliveredDeliveryOrdersForDashboardAsync(
            branchFilter,
            from,
            to,
            cancellationToken);

        var agg = DeliveryDashboardAggregator.Build(orders, from, to);

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
        };
    }

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (_currentUser.Role == "superadmin")
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }
}
