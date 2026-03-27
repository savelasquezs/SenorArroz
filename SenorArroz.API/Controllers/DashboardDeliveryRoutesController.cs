using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Application.Features.Dashboard.Queries;

namespace SenorArroz.API.Controllers;

/// <summary>
/// Detalle de rutas de domicilio para el dashboard (admin, superadmin, domiciliario autenticado).
/// </summary>
[ApiController]
[Route("api/dashboard/delivery")]
[Authorize(Roles = "Admin,Superadmin,Deliveryman")]
public class DashboardDeliveryRoutesController : ControllerBase
{
    private readonly IMediator _mediator;

    public DashboardDeliveryRoutesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Paradas de una ruta cerrada (direcciones / pedidos). Superadmin puede filtrar por <paramref name="branchId"/>.
    /// </summary>
    [HttpGet("routes/{routeId:int}/stops")]
    [ProducesResponseType(typeof(DashboardDeliveryRouteStopsResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardDeliveryRouteStopsResponseDto>> GetRouteStops(
        int routeId,
        [FromQuery] int? branchId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetDashboardDeliveryRouteStopsQuery { RouteId = routeId, BranchId = branchId },
            cancellationToken);

        return Ok(result);
    }
}
