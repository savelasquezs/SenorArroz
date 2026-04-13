using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardSalesComparisonHandler
    : IRequestHandler<GetDashboardSalesComparisonQuery, DashboardSalesComparisonResponseDto>
{
    private const int MaxRangeDays = 400;

    private readonly IOrderRepository _orderRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly ICurrentUser _currentUser;

    public GetDashboardSalesComparisonHandler(
        IOrderRepository orderRepository,
        IBranchRepository branchRepository,
        ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _branchRepository = branchRepository;
        _currentUser = currentUser;
    }

    public async Task<DashboardSalesComparisonResponseDto> Handle(
        GetDashboardSalesComparisonQuery request,
        CancellationToken cancellationToken)
    {
        var (from, to) = ColombiaTimeHelper.NormalizeDashboardRangeUtc(request.FromUtc, request.ToUtc, MaxRangeDays);
        var branchFilter = ResolveBranchFilter(request.BranchId);

        var aggregates = await _orderRepository.GetDashboardSalesComparisonAsync(
            branchFilter,
            from,
            to,
            cancellationToken);

        var allBranches = (await _branchRepository.GetAllAsync(cancellationToken)).OrderBy(b => b.Name).ToList();

        IEnumerable<Branch> branchesToShow;
        if (branchFilter.HasValue)
        {
            branchesToShow = allBranches.Where(b => b.Id == branchFilter.Value);
        }
        else
        {
            branchesToShow = allBranches;
        }

        var aggById = aggregates.ToDictionary(a => a.BranchId);

        var rows = branchesToShow
            .Select(b =>
            {
                if (!aggById.TryGetValue(b.Id, out var a))
                {
                    a = new Domain.Models.BranchSalesComparisonAggregate { BranchId = b.Id };
                }

                return new DashboardSalesComparisonRowDto
                {
                    Id = b.Id,
                    Name = b.Name,
                    SalesTotal = a.SalesTotal,
                    OrdersTotal = a.OrdersTotal,
                    SalesDelivery = a.SalesDelivery,
                    SalesOnsite = a.SalesOnsite,
                    OrdersDelivery = a.OrdersDelivery,
                    OrdersOnsite = a.OrdersOnsite,
                    DeliveryTimeMinutes = 0,
                };
            })
            .ToList();

        return new DashboardSalesComparisonResponseDto { Rows = rows };
    }

    private int? ResolveBranchFilter(int? requestedBranchId)
    {
        if (_currentUser.Role == "superadmin")
            return requestedBranchId;
        return _currentUser.BranchId > 0 ? _currentUser.BranchId : null;
    }
}
