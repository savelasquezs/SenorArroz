using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Application.Features.Dashboard.Queries;

namespace SenorArroz.API.Controllers;

/// <summary>
/// Métricas de desempeño del domiciliario autenticado (mismo contrato que <c>GET /api/dashboard/delivery</c> filtrado a sí mismo).
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = "Deliveryman")]
public class DashboardDeliverySelfController : ControllerBase
{
    private readonly IMediator _mediator;

    public DashboardDeliverySelfController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Evolución y KPIs de entregas del usuario actual. <paramref name="branchId"/> opcional para acotar a una sucursal.
    /// </summary>
    [HttpGet("delivery/me")]
    [ProducesResponseType(typeof(DashboardDeliveryResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardDeliveryResponseDto>> GetDeliveryForSelf(
        [FromQuery(Name = "from")] DateTime fromUtc,
        [FromQuery(Name = "to")] DateTime toUtc,
        [FromQuery] int? branchId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetDashboardDeliveryMeQuery
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                BranchId = branchId,
            },
            cancellationToken);

        return Ok(result);
    }
}
