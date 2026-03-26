using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardDeliveryMeHandler : IRequestHandler<GetDashboardDeliveryMeQuery, DashboardDeliveryResponseDto>
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public GetDashboardDeliveryMeHandler(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    public Task<DashboardDeliveryResponseDto> Handle(
        GetDashboardDeliveryMeQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_currentUser.Role, "deliveryman", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Solo domiciliarios pueden consultar estas métricas.");

        return _mediator.Send(
            new GetDashboardDeliveryQuery
            {
                FromUtc = request.FromUtc,
                ToUtc = request.ToUtc,
                BranchId = request.BranchId,
                DeliveryManId = _currentUser.Id,
            },
            cancellationToken);
    }
}
