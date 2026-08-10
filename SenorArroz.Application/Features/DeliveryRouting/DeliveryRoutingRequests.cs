using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DeliveryRouting.DTOs;

namespace SenorArroz.Application.Features.DeliveryRouting;

public sealed record GetDeliveryRoutingPlanQuery(int? BranchId) : IRequest<DeliveryRoutingPlanDto>;
public sealed record RecalculateDeliveryRoutingPlanCommand(int? BranchId) : IRequest<DeliveryRoutingPlanDto>;
public sealed record PreviewDeliveryRouteQuery(int? BranchId, IReadOnlyList<int> OrderIds) : IRequest<DeliveryRouteProposalDto>;

public sealed class GetDeliveryRoutingPlanHandler : IRequestHandler<GetDeliveryRoutingPlanQuery, DeliveryRoutingPlanDto>
{
    private readonly IBranchContext _branchContext;
    private readonly IDeliveryRoutingPlanService _plans;

    public GetDeliveryRoutingPlanHandler(IBranchContext branchContext, IDeliveryRoutingPlanService plans)
    {
        _branchContext = branchContext;
        _plans = plans;
    }

    public Task<DeliveryRoutingPlanDto> Handle(GetDeliveryRoutingPlanQuery request, CancellationToken cancellationToken) =>
        _plans.GetOrCreateActivePlanAsync(_branchContext.RequireBranch(request.BranchId), cancellationToken);
}

public sealed class RecalculateDeliveryRoutingPlanHandler : IRequestHandler<RecalculateDeliveryRoutingPlanCommand, DeliveryRoutingPlanDto>
{
    private readonly IBranchContext _branchContext;
    private readonly IDeliveryRoutingPlanService _plans;

    public RecalculateDeliveryRoutingPlanHandler(IBranchContext branchContext, IDeliveryRoutingPlanService plans)
    {
        _branchContext = branchContext;
        _plans = plans;
    }

    public Task<DeliveryRoutingPlanDto> Handle(RecalculateDeliveryRoutingPlanCommand request, CancellationToken cancellationToken) =>
        _plans.RecalculateAsync(_branchContext.RequireBranch(request.BranchId), cancellationToken);
}

public sealed class PreviewDeliveryRouteHandler : IRequestHandler<PreviewDeliveryRouteQuery, DeliveryRouteProposalDto>
{
    private readonly IBranchContext _branchContext;
    private readonly IDeliveryRoutingPlanService _plans;

    public PreviewDeliveryRouteHandler(IBranchContext branchContext, IDeliveryRoutingPlanService plans)
    {
        _branchContext = branchContext;
        _plans = plans;
    }

    public Task<DeliveryRouteProposalDto> Handle(PreviewDeliveryRouteQuery request, CancellationToken cancellationToken) =>
        _plans.PreviewAsync(_branchContext.RequireBranch(request.BranchId), request.OrderIds, cancellationToken);
}
