using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.DeliveryRouting;
using SenorArroz.Application.Features.DeliveryRouting.DTOs;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/delivery-routing")]
[Authorize]
public sealed class DeliveryRoutingController : ControllerBase
{
    private readonly IMediator _mediator;

    public DeliveryRoutingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("plan")]
    [Authorize(Roles = "Deliveryman,Admin,Cashier,Superadmin")]
    public async Task<ActionResult<DeliveryRoutingPlanDto>> GetPlan(
        [FromQuery] int? branchId = null,
        CancellationToken cancellationToken = default) =>
        Ok(await _mediator.Send(new GetDeliveryRoutingPlanQuery(branchId), cancellationToken));

    [HttpPost("recalculate")]
    [Authorize(Roles = "Admin,Cashier,Superadmin")]
    public async Task<ActionResult<DeliveryRoutingPlanDto>> Recalculate(
        [FromQuery] int? branchId = null,
        CancellationToken cancellationToken = default) =>
        Ok(await _mediator.Send(new RecalculateDeliveryRoutingPlanCommand(branchId), cancellationToken));

    [HttpPost("preview")]
    [Authorize(Roles = "Deliveryman,Admin,Cashier,Superadmin")]
    public async Task<ActionResult<DeliveryRouteProposalDto>> Preview(
        [FromBody] PreviewDeliveryRouteRequest request,
        [FromQuery] int? branchId = null,
        CancellationToken cancellationToken = default) =>
        Ok(await _mediator.Send(new PreviewDeliveryRouteQuery(branchId, request.OrderIds), cancellationToken));
}
